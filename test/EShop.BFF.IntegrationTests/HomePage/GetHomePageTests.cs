using System.Net;
using System.Net.Http.Json;
using EShop.BFF.Api.Responses;
using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.HomePage;

/// <summary>
/// End-to-end home-page composition over the real typed clients (service-auth + resilience) and the real
/// redis-cache FusionCache, with Catalog search + category tree + Inventory bulk faked by WireMock
/// (issue #328 acceptance: happy path; served-from-cache on repeat; category-tree down → tree null +
/// stale; inventory down → null stock + stale; search down → 503).
/// </summary>
[Collection<HomePageTestCollection>]
public sealed class GetHomePageTests(HomePageTestFixture fixture) : BaseHomePageTest(fixture)
{
    private static readonly Guid LaptopId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MouseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task GetHomePage_WhenAllUpstreamsSucceed_Returns200ComposedPage()
    {
        // Arrange
        Fixture.StubCatalogSearch(SearchBody(LaptopId, MouseId));
        Fixture.StubCategoryTree(CategoryTreeBody());
        Fixture.StubInventoryBulk(BulkBody((LaptopId, 15), (MouseId, 4)));

        // Act
        var page = await GetHomePageAsync();

        // Assert
        using (new AssertionScope())
        {
            page.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            page.Body.Should().NotBeNull();
            page.Body!.HasStaleData.Should().BeFalse();
            page.Body.FeaturedProducts.Should().HaveCount(2);

            var laptop = page.Body.FeaturedProducts.Single(p => p.ProductId == LaptopId);
            laptop.Name.Should().Be("Laptop");
            laptop.Price.Amount.Should().Be(1299.99m);
            laptop.InStock.Should().BeTrue();
            laptop.AvailableQty.Should().Be(15);

            page.Body.CategoryTree.Should().NotBeNull();
            page.Body.CategoryTree!.Nodes.Should().ContainSingle();
            // Only the mouse (qty 4) is "running low" (0 < qty <= 10); the laptop's 15 is not.
            page.Body.StockHighlights.Should().ContainSingle()
                .Which.ProductId.Should().Be(MouseId);
            page.Response.Headers.Contains("X-BFF-PartialData").Should().BeFalse();
            page.Response.Headers.Contains("X-BFF-Stale").Should().BeFalse("a fully-composed page is not stale");
        }
    }

    [Fact]
    public async Task GetHomePage_OnRepeat_IsServedFromCacheWithoutReCallingCatalog()
    {
        // Arrange
        Fixture.StubCatalogSearch(SearchBody(LaptopId, MouseId));
        Fixture.StubCategoryTree(CategoryTreeBody());
        Fixture.StubInventoryBulk(BulkBody((LaptopId, 7), (MouseId, 4)));

        // Act
        var first = await GetHomePageAsync();
        var second = await GetHomePageAsync();

        // Assert
        using (new AssertionScope())
        {
            // Same composition timestamp on the repeat ⇒ the second read came from FusionCache, not a recompose.
            second.Body!.GeneratedAtUtc.Should().Be(first.Body!.GeneratedAtUtc);
            Fixture.CountCatalogSearchCalls().Should().Be(1);
        }
    }

    [Fact]
    public async Task GetHomePage_AfterHomePageTagRemoved_ReComposesFromUpstream()
    {
        // Proves the COMPOSED entry (not just a seeded one) carries the home-page tag, so the bff-group
        // consumer's RemoveByTagAsync("home-page") actually evicts real pages — the load-bearing seam.
        Fixture.StubCatalogSearch(SearchBody(LaptopId, MouseId));
        Fixture.StubCategoryTree(CategoryTreeBody());
        Fixture.StubInventoryBulk(BulkBody((LaptopId, 7), (MouseId, 4)));

        await GetHomePageAsync();                       // compose + cache under tag home-page
        Fixture.CountCatalogSearchCalls().Should().Be(1);

        await Fixture.RemoveHomePageTagAsync();          // the production invalidation path
        await GetHomePageAsync();                        // must hit upstream again

        Fixture.CountCatalogSearchCalls().Should().Be(2, "removing the home-page tag must evict the composed page");
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetHomePage_WhenCategoryTreeIsDown_Returns200WithNullTreeAndStaleData()
    {
        // Arrange
        Fixture.StubCatalogSearch(SearchBody(LaptopId, MouseId));
        Fixture.StubCategoryTreeStatus(statusCode: 500);
        Fixture.StubInventoryBulk(BulkBody((LaptopId, 7), (MouseId, 4)));

        // Act
        var page = await GetHomePageAsync();

        // Assert
        using (new AssertionScope())
        {
            page.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            page.Body!.CategoryTree.Should().BeNull();
            page.Body.FeaturedProducts.Should().HaveCount(2); // featured kept
            page.Body.HasStaleData.Should().BeTrue();
            page.Response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle()
                .Which.Should().Contain("categories");
            // Uniform semantics (bff.md § 2.4): HasStaleData ⇒ X-BFF-Stale, alongside the partial-data header.
            page.Response.Headers.GetValues("X-BFF-Stale").Should().ContainSingle().Which.Should().Be("true");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetHomePage_WhenInventoryBulkIsDown_Returns200WithNullStockAndStaleData()
    {
        // Arrange
        Fixture.StubCatalogSearch(SearchBody(LaptopId, MouseId));
        Fixture.StubCategoryTree(CategoryTreeBody());
        Fixture.StubInventoryBulkStatus(statusCode: 500);

        // Act
        var page = await GetHomePageAsync();

        // Assert
        using (new AssertionScope())
        {
            page.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            page.Body!.FeaturedProducts.Should().AllSatisfy(p =>
            {
                p.InStock.Should().BeNull();
                p.AvailableQty.Should().BeNull();
            });
            page.Body.StockHighlights.Should().BeNull();
            page.Body.HasStaleData.Should().BeTrue();
            page.Response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle()
                .Which.Should().Contain("inventory");
            // Uniform semantics (bff.md § 2.4): HasStaleData ⇒ X-BFF-Stale, alongside the partial-data header.
            page.Response.Headers.GetValues("X-BFF-Stale").Should().ContainSingle().Which.Should().Be("true");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetHomePage_WhenCatalogSearchIsDownAndCachedPageIsStale_ServesStaleWith200AndStaleHeader()
    {
        // Arrange: compose a healthy page, then plant it back as an entry older than its fresh window so a
        // fail-safe serve of it is age-detectable as stale; then take the gating upstream (search) down.
        Fixture.StubCatalogSearch(SearchBody(LaptopId, MouseId));
        Fixture.StubCategoryTree(CategoryTreeBody());
        Fixture.StubInventoryBulk(BulkBody((LaptopId, 15), (MouseId, 4)));

        var fresh = await GetHomePageAsync();
        fresh.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        fresh.Body!.HasStaleData.Should().BeFalse();
        fresh.Response.Headers.Contains("X-BFF-Stale").Should().BeFalse("a freshly composed page is not stale");

        var aged = fresh.Body! with { GeneratedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10) };
        await Fixture.SeedHomePageAsync(aged);
        await Fixture.ExpireHomePageAsync();
        Fixture.ResetUpstreams();
        Fixture.StubCatalogSearchStatus(statusCode: 500);

        // Act: Catalog search is down → native fail-safe serves the expired (aged) page.
        var page = await GetHomePageAsync();

        // Assert: the last-good page is served, flagged stale (200 + HasStaleData + X-BFF-Stale).
        using (new AssertionScope())
        {
            page.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            page.Body.Should().NotBeNull();
            page.Body!.HasStaleData.Should().BeTrue();
            page.Body.FeaturedProducts.Should().HaveCount(2, "the cached page's featured products are still served");
            page.Response.Headers.GetValues("X-BFF-Stale").Should().ContainSingle().Which.Should().Be("true");
            // A healthy cached page has its tree + overlay, so a stale serve of it is not also "partial".
            page.Response.Headers.Contains("X-BFF-PartialData").Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetHomePage_WhenCatalogSearchIsDownAndNoCachedPage_Returns503()
    {
        // Arrange
        Fixture.StubCatalogSearchStatus(statusCode: 500);
        Fixture.StubCategoryTree(CategoryTreeBody());
        Fixture.StubInventoryBulk(BulkBody((LaptopId, 7)));

        // Act
        var response = await Fixture.Client.GetAsync(
            "/api/v1/bff/home-page",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private async Task<(HttpResponseMessage Response, HomePageResponse? Body)> GetHomePageAsync()
    {
        var response = await Fixture.Client.GetAsync(
            "/api/v1/bff/home-page",
            TestContext.Current.CancellationToken);
        var body = response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<HomePageResponse>(TestContext.Current.CancellationToken)
            : null;
        return (response, body);
    }

    private static object SearchBody(params Guid[] productIds) => new
    {
        total = productIds.Length,
        pageNumber = 1,
        pageSize = 20,
        items = productIds.Select((id, index) => new
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

    private static object BulkBody(params (Guid ProductId, int Available)[] items) => new
    {
        items = items.Select(i => new
        {
            productId = i.ProductId,
            onHand = i.Available + 2,
            reserved = 2,
            available = i.Available,
            lastUpdatedUtc = new DateTimeOffset(2026, 06, 15, 0, 0, 0, TimeSpan.Zero),
        }).ToArray(),
        missingProductIds = Array.Empty<Guid>(),
    };
}
