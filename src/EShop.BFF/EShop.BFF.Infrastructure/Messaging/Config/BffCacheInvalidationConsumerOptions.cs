using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace EShop.BFF.Infrastructure.Messaging.Config;

/// <summary>
/// Kafka consumer configuration for the BFF's cache invalidator. Bound from
/// <c>KafkaBffCacheInvalidationConsumer</c>. Inherits <see cref="ConsumerConfig"/> so broker knobs
/// (e.g. <c>AutoOffsetReset</c>) bind directly. The default <c>AutoOffsetReset = Latest</c> is deliberate:
/// invalidation only needs to react to <em>future</em> changes, so a restart must not replay the entire
/// product/category/stock history and needlessly evict the freshly-warmed home page.
/// </summary>
public sealed class BffCacheInvalidationConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaBffCacheInvalidationConsumer";

    /// <summary>
    /// Consumer group id. Per the one-group-per-service rule (events-catalog.md § 3.1) this is
    /// <c>bff-group</c> — the sole BFF consumer group across every topic it subscribes to.
    /// </summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(BffCacheInvalidationConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
