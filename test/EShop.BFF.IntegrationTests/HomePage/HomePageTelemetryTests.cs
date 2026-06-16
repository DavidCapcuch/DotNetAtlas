using System.Net;
using EShop.BFF.Infrastructure.Common.Observability;
using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.HomePage;

/// <summary>
/// End-to-end assertions that the home-page endpoint emits the BFF custom metrics (bff.md § 2.4) over the
/// real endpoint → provider → FusionCache seam: a partial 200 increments <c>bff.partial_response</c>, and a
/// miss-then-hit pair increments <c>bff.cache.misses</c> then <c>bff.cache.hits</c>. The static meter is
/// process-global, so the listener filters on the <c>bff.endpoint=home-page</c> tag — the parallel
/// product-page collection tags <c>product-page</c> and is ignored.
/// </summary>
/// <remarks>
/// The exact <c>==1</c> counts also rely on <see cref="HomePageTestCollection"/> being the <em>only</em>
/// collection that drives the home-page <em>endpoint</em> (the metrics emit there): the parallel
/// invalidation / warm collections exercise the cache, provider, and consumer directly and emit nothing.
/// If a future parallel collection starts <c>GET</c>ing <c>/home-page</c>, swap these for delta assertions.
/// </remarks>
[Collection<HomePageTestCollection>]
public sealed class HomePageTelemetryTests(HomePageTestFixture fixture) : BaseHomePageTest(fixture)
{
    private static readonly Guid LaptopId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MouseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetHomePage_WhenInventoryBulkIsDown_IncrementsPartialResponseMetric()
    {
        // Arrange — search + tree healthy, Inventory bulk down → the page 200s with partial (stale) data.
        Fixture.StubCatalogSearch(SearchBody());
        Fixture.StubCategoryTree(CategoryTreeBody());
        Fixture.StubInventoryBulkStatus(statusCode: 500);
        using var metrics = new BffEndpointCounters(BffMetrics.HomePageEndpoint, "bff.partial_response");

        // Act
        var response = await Fixture.Client.GetAsync(
            "/api/v1/bff/home-page",
            TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Contain("inventory");
        metrics.Total("bff.partial_response").Should().Be(1,
            "an inventory-down 200 with partial data must increment bff.partial_response for the home-page endpoint");
    }

    [Fact]
    public async Task GetHomePage_FirstRequestMissesThenSecondHits_RecordsCacheMetrics()
    {
        // Arrange — all upstreams healthy so the page composes and caches cleanly.
        Fixture.StubCatalogSearch(SearchBody());
        Fixture.StubCategoryTree(CategoryTreeBody());
        Fixture.StubInventoryBulk(BulkBody());
        using var metrics = new BffEndpointCounters(
            BffMetrics.HomePageEndpoint, "bff.cache.hits", "bff.cache.misses");

        // Act
        await Fixture.Client.GetAsync("/api/v1/bff/home-page", TestContext.Current.CancellationToken);
        var missesAfterFirst = metrics.Total("bff.cache.misses");
        await Fixture.Client.GetAsync("/api/v1/bff/home-page", TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        missesAfterFirst.Should().Be(1, "the first request composes the page (cache miss)");
        metrics.Total("bff.cache.hits").Should().Be(1, "the second request is served from cache (cache hit)");
        metrics.Total("bff.cache.misses").Should().Be(1, "the second request must not re-compose");
    }

    private static object SearchBody() => new
    {
        total = 2,
        pageNumber = 1,
        pageSize = 20,
        items = new[] { LaptopId, MouseId }.Select((id, index) => new
        {
            productId = id,
            sku = $"SKU-{index}",
            name = id == LaptopId ? "Laptop" : "Mouse",
            categoryBreadcrumb = "Electronics > Computers",
            brandName = "Acme",
            price = new { amount = 1299.99m, currency = "USD" },
            status = "Active",
            primaryImageUrl = $"https://cdn/{id}.jpg",
        }).ToArray(),
    };

    private static object CategoryTreeBody() => new
    {
        nodes = new[]
        {
            new
            {
                categoryId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                name = "Electronics",
                path = "/electronics",
                parentCategoryId = (Guid?)null,
                depth = 0,
                productCount = 12,
            },
        },
    };

    private static object BulkBody() => new
    {
        items = new[] { (LaptopId, 15), (MouseId, 4) }.Select(i => new
        {
            productId = i.Item1,
            onHand = i.Item2 + 2,
            reserved = 2,
            available = i.Item2,
            lastUpdatedUtc = new DateTimeOffset(2026, 06, 15, 0, 0, 0, TimeSpan.Zero),
        }).ToArray(),
        missingProductIds = Array.Empty<Guid>(),
    };
}
