using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Infrastructure.Caching;

/// <summary>
/// The home page's FusionCache entry policy (bff.md § 3.4). Distinct from the product-page default
/// (<see cref="BffCacheDependencyInjection"/>): a longer-lived, eagerly-refreshed entry — the home page
/// is the most-hit endpoint, so cache-hit rate is the goal and a background refresh at 80% of TTL
/// prevents a cache-miss latency spike when the entry expires.
/// </summary>
public static class BffHomePageCache
{
    private static readonly TimeSpan SoftTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailSafeMaxDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan JitterMaxDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Duration for a degraded compose (category tree or stock overlay unavailable): short, so a recovered
    /// upstream re-composes the full page quickly instead of pinning a partial one for the 5-minute TTL.
    /// </summary>
    public static readonly TimeSpan DegradedDuration = TimeSpan.FromSeconds(30);

    /// <summary>The single home-page tag, as the tag set the entry is written under.</summary>
    public static readonly string[] Tags = [BffCacheConstants.HomePageTag];

    /// <summary>Fresh entry options for a healthy compose (5-min TTL, fail-safe 30 min, eager refresh at 80%).</summary>
    public static FusionCacheEntryOptions EntryOptions() =>
        new()
        {
            Duration = SoftTtl,
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = FailSafeMaxDuration,
            JitterMaxDuration = JitterMaxDuration,

            // Background-refresh the home page once it is 80% through its TTL so the most-hit endpoint
            // never serves a synchronous cache miss (bff.md § 3.4).
            EagerRefreshThreshold = 0.8f,

            // A volatile-cache hiccup must degrade the read (recompute from upstreams), never 5xx it.
            ReThrowDistributedCacheExceptions = false,
            ReThrowSerializationExceptions = false,
        };
}
