using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.BFF.Api.Responses;
using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.ProductPage;

/// <summary>
/// The authenticated product-page basket overlay (bff.md § 3.1, issue #330): an authenticated buyer also
/// gets <c>AlreadyInBasket</c> / <c>BasketQuantity</c> from a per-request, never-cached Basket read (via the
/// <c>basket.read</c> exchange). Anonymous callers are unaffected, and a Basket failure must never break the
/// page — it degrades to <c>AlreadyInBasket: null</c> + <c>X-BFF-PartialData: basket</c>, still 200.
/// </summary>
[Collection<ProductPageTestCollection>]
public sealed class ProductPageBasketOverlayTests(ProductPageTestFixture fixture) : BaseProductPageTest(fixture)
{
    private static readonly DateTimeOffset StubTimestamp = new(2026, 06, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProductPage_WhenAuthenticatedAndItemInBasket_SetsAlreadyInBasketWithQuantity()
    {
        // Arrange
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));
        Fixture.StubBasket(BasketBody(Item(productId, quantity: 3)));

        // Act
        var page = await GetAsAuthenticatedAsync(productId, Guid.NewGuid());

        // Assert
        using (new AssertionScope())
        {
            page.AlreadyInBasket.Should().BeTrue();
            page.BasketQuantity.Should().Be(3);
        }
    }

    [Fact]
    public async Task ProductPage_WhenAuthenticatedAndItemNotInBasket_SetsAlreadyInBasketFalse()
    {
        // Arrange — basket holds a different product.
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));
        Fixture.StubBasket(BasketBody(Item(Guid.NewGuid(), quantity: 1)));

        // Act
        var page = await GetAsAuthenticatedAsync(productId, Guid.NewGuid());

        // Assert
        using (new AssertionScope())
        {
            page.AlreadyInBasket.Should().BeFalse();
            page.BasketQuantity.Should().BeNull();
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task ProductPage_WhenAuthenticatedAndNoBasketYet_SetsAlreadyInBasketFalseWithoutPartialHeader()
    {
        // Arrange — Basket 404 = the buyer has no basket: the product is definitively not in it.
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));
        Fixture.StubBasketStatus(404);

        // Act
        var (response, page) = await GetWithResponseAsync(productId, Guid.NewGuid());

        // Assert
        using (new AssertionScope())
        {
            page.AlreadyInBasket.Should().BeFalse();
            page.BasketQuantity.Should().BeNull();
            response.Headers.Contains("X-BFF-PartialData").Should().BeFalse("a 404 basket is a definitive answer, not a degraded one");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task ProductPage_WhenAuthenticatedAndBasketIsDown_DegradesToNullWithPartialHeaderStill200()
    {
        // Arrange — Basket 5xx on the auth path must not break the public page (bff.md § 3.1).
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));
        Fixture.StubBasketStatus(500);

        // Act
        var (response, page) = await GetWithResponseAsync(productId, Guid.NewGuid());

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            page.Product.ProductId.Should().Be(productId, "the catalog/inventory composite still renders");
            page.AlreadyInBasket.Should().BeNull();
            response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Be("basket");
            response.Headers.Contains("X-BFF-Stale")
                .Should().BeFalse("a missing per-request basket overlay does not make the cached product page stale");
        }
    }

    [Fact]
    public async Task ProductPage_WhenAnonymous_LeavesBasketFieldsNullAndMakesNoBasketCall()
    {
        // Arrange — no Basket stub at all; an anonymous request must not call Basket.
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));

        // Act — no Authorization header.
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}", TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await ReadAsync(response);
            page.AlreadyInBasket.Should().BeNull();
            page.BasketQuantity.Should().BeNull();
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task ProductPage_WhenLaterCallerIsAnonymous_BasketOverlayIsNotLeaked()
    {
        // Arrange — an authenticated caller whose basket holds the item caches the anonymous composite.
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));
        Fixture.StubBasket(BasketBody(Item(productId, quantity: 9)));

        var authed = await GetAsAuthenticatedAsync(productId, Guid.NewGuid());
        authed.AlreadyInBasket.Should().BeTrue("precondition: the authenticated caller sees their basket");

        // Act — a later anonymous request hits the same cached page.
        var anon = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}", TestContext.Current.CancellationToken);
        var anonPage = await ReadAsync(anon);

        // Assert — the per-request overlay was never written into the shared cache entry.
        anonPage.AlreadyInBasket.Should().BeNull("the basket overlay is per-request, never cached");
    }

    private async Task<ProductPageResponse> GetAsAuthenticatedAsync(Guid productId, Guid userId)
    {
        var (response, page) = await GetWithResponseAsync(productId, userId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return page;
    }

    private async Task<(HttpResponseMessage Response, ProductPageResponse Page)> GetWithResponseAsync(
        Guid productId, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/bff/product-page/{productId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Fixture.CreateUserToken(userId));
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        return (response, await ReadAsync(response));
    }

    private static async Task<ProductPageResponse> ReadAsync(HttpResponseMessage response)
    {
        var page = await response.Content.ReadFromJsonAsync<ProductPageResponse>(TestContext.Current.CancellationToken);
        page.Should().NotBeNull();
        return page!;
    }

    private static object BasketBody(params object[] items) => new
    {
        userId = Guid.NewGuid(),
        version = 2,
        items,
        total = new { amount = 0m, currency = "USD" },
        createdAtUtc = StubTimestamp,
        lastModifiedAtUtc = StubTimestamp,
    };

    private static object Item(Guid productId, int quantity) => new
    {
        productId,
        sku = "SKU-1",
        name = "Item",
        snapshotPrice = new { amount = 10m, currency = "USD" },
        quantity,
        capturedAtUtc = StubTimestamp,
        lineTotal = new { amount = 10m * quantity, currency = "USD" },
    };

    private static object CatalogBody(Guid productId) => new
    {
        productId,
        sku = "SKU-1",
        name = "Laptop",
        description = "A fast laptop",
        brandName = "Acme",
        categoryPath = "/electronics",
        categoryBreadcrumb = "Electronics",
        price = new { amount = 1299.99m, currency = "USD" },
        status = "Active",
        dimensions = (object?)null,
        images = new[] { new { url = "https://cdn/img.jpg", altText = "img", displayOrder = 0 } },
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
