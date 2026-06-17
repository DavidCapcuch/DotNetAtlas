using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Infrastructure.Caching;

/// <summary>
/// The basket page's FusionCache entry policy (bff.md § 3.2): a short, <b>per-user</b> entry. Baskets are
/// ephemeral and users want near-real-time feedback after mutations, so a 15-second soft TTL with a
/// 2-minute fail-safe window backstops out-of-band changes (e.g. the checkout consumer clearing the basket).
/// No jitter (per-user keys → no thundering herd) and no eager refresh (a basket is not a shared hot entry).
/// The primary freshness mechanism is synchronous tag invalidation on BFF-mediated mutations (bff.md § 3.6);
/// this TTL is the backstop.
/// </summary>
public static class BffBasketCache
{
    private static readonly TimeSpan SoftTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FailSafeMaxDuration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The oldest a still-fresh basket entry can be (soft TTL, no jitter). A basket older than this was
    /// served from fail-safe, so the endpoint flags it stale (bff.md § 3.2); see <c>StaleServePolicy</c>.
    /// </summary>
    public static readonly TimeSpan StaleServeFreshWindow = SoftTtl;

    /// <summary>Fresh entry options for a basket compose (15-second TTL, fail-safe 2 minutes).</summary>
    public static FusionCacheEntryOptions EntryOptions() =>
        new()
        {
            Duration = SoftTtl,
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = FailSafeMaxDuration,
            JitterMaxDuration = TimeSpan.Zero,

            // A volatile-cache hiccup must degrade the read (recompute from upstreams), never 5xx it.
            ReThrowDistributedCacheExceptions = false,
            ReThrowSerializationExceptions = false,
        };

    /// <summary>The per-user basket-page tag set the entry is written under.</summary>
    public static string[] Tags(Guid userId) => [BffCacheConstants.BasketPageTag(userId)];
}
