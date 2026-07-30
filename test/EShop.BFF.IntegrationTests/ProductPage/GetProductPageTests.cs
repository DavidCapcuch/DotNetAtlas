using System.Net;
using System.Net.Http.Json;
using EShop.BFF.Api.Responses;
using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.ProductPage;

/// <summary>
/// End-to-end product-page composition over the real typed clients (service-auth + resilience) and the
/// real redis-cache FusionCache, with Catalog + Inventory faked by WireMock (issue #327 acceptance:
/// happy path, Catalog 404, Inventory down → stale/partial; plus Catalog down → 503).
/// </summary>
[Collection<ProductPageTestCollection>]
public sealed class GetProductPageTests(ProductPageTestFixture fixture) : BaseProductPageTest(fixture)
{
    private static readonly DateTimeOffset StubTimestamp = new(2026, 06, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task GetProductPage_WhenCatalogAndInventorySucceed_Returns200ComposedPage()
    {
        // Arrange
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));

        // Act
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ProductPageResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            page.Should().NotBeNull();
            page!.Product.ProductId.Should().Be(productId);
            page.Product.Name.Should().Be("Laptop");
            page.Product.CategoryBreadcrumb.Should().Be("Electronics > Computers > Laptops");
            page.Product.Price.Amount.Should().Be(1299.99m);
            page.Product.Images.Should().ContainSingle();
            page.InStock.Should().BeTrue();
            page.AvailableQty.Should().Be(7);
            page.HasStaleData.Should().BeFalse();
            response.Headers.Contains("X-BFF-PartialData").Should().BeFalse();
            response.Headers.Contains("X-BFF-Stale").Should().BeFalse("a fully-composed page is not stale");
        }
    }

    [Fact]
    public async Task GetProductPage_WhenCatalogReturns404_Returns404()
    {
        // Arrange
        var productId = Guid.NewGuid();
        Fixture.StubCatalogStatus(productId, statusCode: 404);
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 5));

        // Act
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductPage_WhenInventoryIsDown_Returns200PartialWithStaleData()
    {
        // Arrange
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStatus(productId, statusCode: 500);

        // Act
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ProductPageResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            page.Should().NotBeNull();
            page!.Product.ProductId.Should().Be(productId);
            page.InStock.Should().BeNull();
            page.AvailableQty.Should().BeNull();
            page.HasStaleData.Should().BeTrue();
            response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Be("inventory");
            // Uniform semantics (bff.md § 2.4): HasStaleData ⇒ X-BFF-Stale, alongside the partial-data header.
            response.Headers.GetValues("X-BFF-Stale").Should().ContainSingle().Which.Should().Be("true");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductPage_WhenCatalogIsDownAndCachedPageIsStale_ServesStaleWith200AndStaleHeader()
    {
        // Arrange: compose a healthy page, then plant it back as an entry older than its fresh window so a
        // fail-safe serve of it is age-detectable as stale; then take the gating upstream (Catalog) down.
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));

        var fresh = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);
        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
        fresh.Headers.Contains("X-BFF-Stale").Should().BeFalse("a freshly composed page is not stale");
        var freshBody = await fresh.Content.ReadFromJsonAsync<ProductPageResponse>(
            TestContext.Current.CancellationToken);

        var aged = freshBody! with { GeneratedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10) };
        await Fixture.SeedProductPageAsync(productId, aged);
        await Fixture.ExpireProductPageAsync(productId);
        Fixture.ResetUpstreams();
        Fixture.StubCatalogStatus(productId, statusCode: 500);

        // Act: Catalog is down → native fail-safe serves the expired (aged) page.
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);

        // Assert: the last-good page is served, flagged stale (200 + HasStaleData + X-BFF-Stale).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ProductPageResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            page.Should().NotBeNull();
            page!.Product.ProductId.Should().Be(productId);
            page.Product.Name.Should().Be("Laptop"); // the last-good cached composition
            page.HasStaleData.Should().BeTrue();
            response.Headers.GetValues("X-BFF-Stale").Should().ContainSingle().Which.Should().Be("true");
        }
    }

    /// <summary>The two ways Catalog reports a product with no dimensions.</summary>
    public enum AbsentDimensions
    {
        Null,
        Omitted,
    }

    [Theory]
    [Trait("Category", "boundary")]
    [InlineData(AbsentDimensions.Null)]
    [InlineData(AbsentDimensions.Omitted)]
    public async Task GetProductPage_WhenProductHasNoDimensions_Returns200WithNullDimensions(
        AbsentDimensions shape)
    {
        // Arrange — a digital/service product. Binding is strict, so an optional upstream member has to be
        // declared nullable or every such product would fail the page; this pins that it is.
        var productId = Guid.NewGuid();
        var body = CatalogBody(productId);
        if (shape == AbsentDimensions.Null)
        {
            body["dimensions"] = null;
        }
        else
        {
            body.Remove("dimensions");
        }

        Fixture.StubCatalogProduct(productId, body);
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 4));

        // Act
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ProductPageResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            page.Should().NotBeNull();
            page!.Product.Dimensions.Should().BeNull();
            page.Product.Price.Amount.Should().Be(1299.99m, "the rest of the product still binds");
            page.AvailableQty.Should().Be(4);
            page.HasStaleData.Should().BeFalse("an absent optional member is not a degradation");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductPage_WhenCatalogDropsAMemberThePageRenders_Returns503()
    {
        // Arrange — Catalog answers 200 without a price. Catalog gates this page, so an unbindable payload
        // has to fail closed the way an unreachable Catalog does, not render a page with a missing price.
        var productId = Guid.NewGuid();
        var body = CatalogBody(productId);
        body.Remove("price");
        Fixture.StubCatalogProduct(productId, body);
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 5));

        // Act
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductPage_WhenCatalogIsDownAndNoCachedPage_Returns503()
    {
        // Arrange
        var productId = Guid.NewGuid();
        Fixture.StubCatalogStatus(productId, statusCode: 500);
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 5));

        // Act
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// The single-product payload as Catalog emits it. Keyed rather than anonymous so a test can drop one
    /// member to model a contract change.
    /// </summary>
    private static Dictionary<string, object?> CatalogBody(Guid productId) => new()
    {
        ["productId"] = productId,
        ["sku"] = "SKU-1",
        ["name"] = "Laptop",
        ["description"] = "A fast laptop",
        ["brandName"] = "Acme",
        ["categoryPath"] = "/electronics/computers/laptops",
        ["categoryBreadcrumb"] = "Electronics > Computers > Laptops",
        ["price"] = new { amount = 1299.99m, currency = "USD" },
        ["status"] = "Active",
        ["dimensions"] = new { length = 35.5m, width = 24.0m, height = 2.0m, unit = "cm" },
        ["images"] = new[] { new { url = "https://cdn/img-1.jpg", altText = "Laptop front", displayOrder = 0 } },
    };

    private static object InventoryBody(Guid productId, int available) => new
    {
        productId,
        onHand = available + 3,
        reserved = 3,
        available,
        lastUpdatedUtc = StubTimestamp,
        lastVersion = 4,
    };
}
