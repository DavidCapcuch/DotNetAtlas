namespace EShop.BFF.Infrastructure.Caching;

/// <summary>Names + connection-string key for the BFF composed-response cache (bff.md § 3.1.1).</summary>
internal static class BffCacheConstants
{
    /// <summary>Key for the keyed redis-cache <c>IConnectionMultiplexer</c> (distributed cache + backplane).</summary>
    public const string CacheName = "bff";

    /// <summary>
    /// Config connection-string name: <c>ConnectionStrings:Redis:Cache</c> (ADR-0016 — the volatile
    /// <c>redis-cache</c> instance, NEVER <c>redis-basket</c>). The colon-bearing path is a literal key.
    /// </summary>
    public const string RedisCacheConnectionStringName = "Redis:Cache";

    /// <summary>FusionCache key for the single, shared anonymous home page (bff.md § 3.4).</summary>
    public const string HomePageKey = "home-page:v1";

    /// <summary>FusionCache key for a single product page (bff.md § 3.1.1).</summary>
    public static string ProductPageKey(Guid productId) => $"product-page:{productId}";

    /// <summary>
    /// FusionCache tag on the home-page entry. The <c>bff-group</c> Kafka invalidator removes this tag
    /// when an upstream change may have altered the page (bff.md § 2.2 / § 3.4).
    /// </summary>
    public const string HomePageTag = "home-page";

    /// <summary>FusionCache key for a buyer's enriched basket page (bff.md § 3.2.1). Per-user.</summary>
    public static string BasketPageKey(Guid userId) => $"basket-bff:{userId}";

    /// <summary>
    /// FusionCache tag on a buyer's basket-page entry — <c>basket-bff-{userId}</c>. The BFF removes it
    /// synchronously on every basket mutation it fronts (bff.md § 3.6, later slices) and the
    /// <c>bff-group</c> invalidator removes it on <c>BasketCheckoutInitiatedEvent</c> (bff.md § 2.2 / § 3.2).
    /// </summary>
    public static string BasketPageTag(Guid userId) => $"basket-bff-{userId}";
}
