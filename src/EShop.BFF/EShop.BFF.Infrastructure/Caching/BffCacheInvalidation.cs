using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Infrastructure.Caching;

/// <summary>
/// Best-effort BFF cache-tag eviction for the request-side (synchronous) invalidation a basket mutation
/// triggers (bff.md § 3.6). The mutation has <b>already committed</b> in Basket by the time this runs, so
/// the eviction is post-commit maintenance: it takes no cancellation token — a client disconnecting right
/// after Basket committed must not abort the eviction and leave the stale page cached — and a transient
/// <c>redis-cache</c> fault (including a cancellation surfacing from the cache's own internals) is logged
/// and swallowed, never propagated, or coherence maintenance would turn a succeeded write into a 5xx (and a
/// client retry of a non-idempotent add). The short TTL + the <c>basket.sessions</c> Kafka invalidator
/// backstop a missed eviction. Same contract as the consume-side <see cref="Messaging.CacheInvalidatorBase"/>.
/// </summary>
internal static class BffCacheInvalidation
{
    public static async Task TryRemoveByTagAsync(IFusionCache cache, string tag, ILogger logger)
    {
        try
        {
            await cache.RemoveByTagAsync(tag, token: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Best-effort BFF cache invalidation of tag {Tag} failed; TTL backstops staleness", tag);
        }
    }
}
