using Avro.Specific;
using Catalog.Categories;
using Catalog.Products;
using EShop.BFF.Infrastructure.Caching;
using Inventory.Stock;

namespace EShop.BFF.Infrastructure.Messaging;

/// <summary>
/// The event → FusionCache-tag mapping the <c>bff-group</c> invalidator applies (bff.md § 2.2 / § 3.4),
/// extracted as a pure function so the invalidation contract is unit-testable independently of Kafka.
/// </summary>
/// <remarks>
/// This slice maps the home-page-relevant subset: every Catalog product/category lifecycle event and every
/// Inventory stock-level change may alter the featured set, the category tree, or availability, so each
/// removes the <c>home-page</c> tag. The product-page <c>product-{id}</c> tags from the full § 2.2 map are
/// the product-page invalidation slice's concern (deferred) — add them here when that slice lands.
/// </remarks>
internal static class CacheInvalidationTagMap
{
    private static readonly string[] HomePage = [BffCacheConstants.HomePageTag];

    /// <summary>The cache tags to remove for <paramref name="message"/>; empty for an unmapped event.</summary>
    public static IReadOnlyList<string> TagsFor(ISpecificRecord message) => message switch
    {
        ProductCreatedEvent or ProductPriceChangedEvent or ProductDiscontinuedEvent => HomePage,
        CategoryCreatedEvent => HomePage,
        StockLevelChangedEvent => HomePage,
        _ => [],
    };
}
