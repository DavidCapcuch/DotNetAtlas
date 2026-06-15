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
}
