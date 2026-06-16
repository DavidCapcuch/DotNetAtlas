using System.Net;
using EShop.BFF.Infrastructure.Common.Observability;
using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.ProductPage;

/// <summary>
/// End-to-end assertions that the product-page endpoint emits the BFF custom metrics (bff.md § 2.4) over
/// the real endpoint → FusionCache seam: a partial 200 increments <c>bff.partial_response</c>, and a
/// miss-then-hit pair increments <c>bff.cache.misses</c> then <c>bff.cache.hits</c>. The listener filters
/// on <c>bff.endpoint=product-page</c> so the parallel home-page collection never leaks in.
/// </summary>
/// <remarks>
/// The exact <c>==1</c> counts also rely on <see cref="ProductPageTestCollection"/> being the only
/// collection that drives the product-page endpoint (where the metrics emit). Each test uses a fresh
/// <c>productId</c>, so the two cache reads in the miss-then-hit case are this test's own.
/// </remarks>
[Collection<ProductPageTestCollection>]
public sealed class ProductPageTelemetryTests(ProductPageTestFixture fixture) : BaseProductPageTest(fixture)
{
    private static readonly DateTimeOffset StubTimestamp = new(2026, 06, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetProductPage_WhenInventoryIsDown_IncrementsPartialResponseMetric()
    {
        // Arrange — Catalog healthy, Inventory down → the page 200s with null availability (partial/stale).
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStatus(productId, statusCode: 500);
        using var metrics = new BffEndpointCounters(BffMetrics.ProductPageEndpoint, "bff.partial_response");

        // Act
        var response = await Fixture.Client.GetAsync(
            $"/api/v1/bff/product-page/{productId}",
            TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Be("inventory");
        metrics.Total("bff.partial_response").Should().Be(1,
            "an inventory-down 200 with partial data must increment bff.partial_response for the product-page endpoint");
    }

    [Fact]
    public async Task GetProductPage_FirstRequestMissesThenSecondHits_RecordsCacheMetrics()
    {
        // Arrange — both upstreams healthy so the page composes and caches cleanly.
        var productId = Guid.NewGuid();
        Fixture.StubCatalogProduct(productId, CatalogBody(productId));
        Fixture.StubInventoryStock(productId, InventoryBody(productId, available: 7));
        using var metrics = new BffEndpointCounters(
            BffMetrics.ProductPageEndpoint, "bff.cache.hits", "bff.cache.misses");

        // Act
        await Fixture.Client.GetAsync($"/api/v1/bff/product-page/{productId}", TestContext.Current.CancellationToken);
        var missesAfterFirst = metrics.Total("bff.cache.misses");
        await Fixture.Client.GetAsync($"/api/v1/bff/product-page/{productId}", TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        missesAfterFirst.Should().Be(1, "the first request composes the page (cache miss)");
        metrics.Total("bff.cache.hits").Should().Be(1, "the second request is served from cache (cache hit)");
        metrics.Total("bff.cache.misses").Should().Be(1, "the second request must not re-compose");
    }

    private static object CatalogBody(Guid productId) => new
    {
        productId,
        sku = "SKU-1",
        name = "Laptop",
        description = "A fast laptop",
        brandName = "Acme",
        categoryPath = "/electronics/computers/laptops",
        categoryBreadcrumb = "Electronics > Computers > Laptops",
        price = new { amount = 1299.99m, currency = "USD" },
        status = "Active",
        dimensions = new { length = 35.5m, width = 24.0m, height = 2.0m, unit = "cm" },
        images = new[] { new { url = "https://cdn/img-1.jpg", altText = "Laptop front", displayOrder = 0 } },
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
