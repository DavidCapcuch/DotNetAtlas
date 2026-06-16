using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Api.Composition;

/// <summary>
/// The home page's read-through cache + upstream orchestration (bff.md § 3.4), shared by
/// <c>GetHomePageEndpoint</c> and the eager-warm hosted service so both populate the same
/// <c>home-page:v1</c> entry with identical policy. Catalog search gates the page (transport failure →
/// fail-safe stale, else <see cref="UpstreamUnavailableException"/>); the category tree and Inventory
/// bulk overlay are non-gating enrichments whose failure degrades the page rather than failing it.
/// </summary>
internal sealed class HomePageProvider
{
    // "Featured" v1 = the first page of active products in Catalog search's default order (bff.md § 3.4
    // posits CreatedAtUtc-desc; Catalog currently orders by price and exposes no sort knob — a dedicated
    // featured ranking is planned scope). The BFF passes status + paging and renders whatever order it gets.
    private const int FeaturedPageSize = 20;
    private static readonly SearchProductsRequest FeaturedQuery =
        new(Status: "Active", PageNumber: 1, PageSize: FeaturedPageSize);

    private readonly ICatalogClient _catalog;
    private readonly IInventoryClient _inventory;
    private readonly IFusionCache _cache;
    private readonly TimeProvider _timeProvider;

    public HomePageProvider(
        ICatalogClient catalog,
        IInventoryClient inventory,
        IFusionCache cache,
        TimeProvider timeProvider)
    {
        _catalog = catalog;
        _inventory = inventory;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Returns the cached home page (with whether it was a cache hit), composing it from upstreams on a
    /// miss. <c>CacheHit</c> is <c>true</c> when FusionCache served the entry without running the factory —
    /// the signal behind <c>bff.cache.hits/misses</c> and the <c>bff.cache.hit</c> span tag (bff.md § 2.4);
    /// the eager-warm hosted service discards it (a background warm is not a user-facing read). Throws
    /// <see cref="UpstreamUnavailableException"/> only when Catalog search is down and no stale page exists
    /// to fail-safe to.
    /// </summary>
    public async Task<(HomePageResponse Page, bool CacheHit)> GetOrComposeAsync(CancellationToken ct)
    {
        // The factory runs only on a miss; capture whether it ran so the caller can attribute the read.
        // A fail-safe stale serve runs the factory (it threw), so it counts as a miss — consistent with
        // "a cache hit returned from cache without making upstream calls" (bff.md § 2.4).
        var factoryRan = false;

        var page = await _cache.GetOrSetAsync<HomePageResponse>(
            BffCacheConstants.HomePageKey,
            (ctx, factoryCt) =>
            {
                factoryRan = true;
                return ComposeAsync(ctx, factoryCt);
            },
            options: BffHomePageCache.EntryOptions(),
            tags: BffHomePageCache.Tags,
            token: ct);

        // FusionCache's native fail-safe serves the last-good page (with its compose-time flags) when
        // Catalog search is down. It exposes no "served stale" signal, so flag it from the page's age:
        // a page older than its fresh window can only have come from fail-safe (bff.md § 3.4 / § 2.4).
        if (StaleServePolicy.WasServedStale(page.GeneratedAtUtc, _timeProvider.GetUtcNow(), BffHomePageCache.StaleServeFreshWindow))
        {
            page = page with { HasStaleData = true };
        }

        return (page, CacheHit: !factoryRan);
    }

    private async Task<HomePageResponse> ComposeAsync(
        FusionCacheFactoryExecutionContext<HomePageResponse> ctx, CancellationToken ct)
    {
        // Search (gating) + category tree (non-gating) run in parallel; the bulk stock overlay depends on
        // the search result (the featured ids), so it is the one sequential step (bff.md § 3.4).
        var searchTask = _catalog.SearchProductsAsync(FeaturedQuery, ct);
        var treeTask = _catalog.GetCategoryTreeAsync(rootCategoryId: null, ct);

        var searchResult = await searchTask;
        if (searchResult.IsFailed)
        {
            // Catalog search is down → don't cache a failure; let fail-safe serve a stale page if any
            // (else this surfaces and the endpoint maps it to 503).
            throw new UpstreamUnavailableException("catalog-search");
        }

        var featured = searchResult.Value.Items;
        var stockOrNull = await ResolveStockOverlayAsync(featured, ct);

        var treeResult = await treeTask;
        var treeOrNull = treeResult.IsSuccess ? treeResult.Value : null;

        var page = HomePageComposer.Compose(featured, treeOrNull, stockOrNull, _timeProvider.GetUtcNow());

        // A partial page (category tree or stock overlay missing) must not be pinned for the full
        // 5-minute TTL — shorten it so a recovered upstream re-composes the full page quickly.
        if (treeOrNull is null || stockOrNull is null)
        {
            ctx.Options.SetDuration(BffHomePageCache.DegradedDuration);
        }

        return page;
    }

    private async Task<StockLevelsBulkDto?> ResolveStockOverlayAsync(
        IReadOnlyList<CatalogProductSummaryDto> featured, CancellationToken ct)
    {
        var productIds = featured.Select(product => product.ProductId).ToList();
        if (productIds.Count == 0)
        {
            // Nothing to overlay — an empty (not absent) overlay keeps the page non-degraded.
            return new StockLevelsBulkDto([], []);
        }

        var bulkResult = await _inventory.GetStockLevelsBulkAsync(productIds, ct);
        return bulkResult.IsSuccess ? bulkResult.Value : null;
    }
}
