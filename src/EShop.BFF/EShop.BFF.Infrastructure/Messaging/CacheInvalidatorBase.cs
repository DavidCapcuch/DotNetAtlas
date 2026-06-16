using Avro.Specific;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Infrastructure.Messaging;

/// <summary>
/// Shared behaviour for the <c>bff-group</c> cache invalidators: resolve the tags for a consumed event
/// (<see cref="CacheInvalidationTagMap"/>) and remove them from FusionCache. Invalidation is a best-effort,
/// idempotent set-membership removal — a transient cache fault is logged and swallowed rather than wedging
/// the consumer (there is no DLT, by design — bff.md § 2.2); the entry's soft TTL backstops staleness.
/// </summary>
internal abstract class CacheInvalidatorBase
{
    private readonly IFusionCache _cache;
    private readonly ILogger _logger;

    protected CacheInvalidatorBase(IFusionCache cache, ILogger logger)
    {
        _cache = cache;
        _logger = logger;
    }

    protected async Task InvalidateAsync(ISpecificRecord message, IMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        var ct = context.ConsumerContext.WorkerStopped;
        var eventName = message.GetType().Name;

        foreach (var tag in CacheInvalidationTagMap.TagsFor(message))
        {
            try
            {
                await _cache.RemoveByTagAsync(tag, token: ct);
                _logger.LogDebug("Invalidated BFF cache tag {Tag} on {Event}", tag, eventName);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // graceful shutdown — let KafkaFlow stop the worker
            }
            catch (Exception ex)
            {
                // Cache invalidation is non-critical: the soft TTL backstops any missed eviction, so a
                // redis-cache hiccup must not wedge the consumer (no DLT to park the message — bff.md § 2.2).
                _logger.LogWarning(
                    ex, "Best-effort invalidation of cache tag {Tag} on {Event} failed; TTL backstops staleness",
                    tag, eventName);
            }
        }
    }
}
