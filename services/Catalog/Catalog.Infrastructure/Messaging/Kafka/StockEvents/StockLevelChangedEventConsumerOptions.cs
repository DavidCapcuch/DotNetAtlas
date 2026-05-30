using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Catalog.Infrastructure.Messaging.Kafka.StockEvents;

/// <summary>
/// Kafka consumer configuration for the inbound <c>StockLevelChangedEvent</c> event from
/// Inventory. Bound from <c>KafkaStockLevelChangedEventConsumer</c>. Inherits from
/// <see cref="ConsumerConfig"/> so broker-level knobs (auto-offset-reset, session-timeout)
/// are bindable directly.
/// </summary>
public sealed class StockLevelChangedEventConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaStockLevelChangedEventConsumer";

    /// <summary>Inbound topic — owned by the Inventory bounded context.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>
    /// Consumer group id. Must NOT collide with Inventory's own internal groups
    /// (per <c>events-catalog.md § 7</c>) — recommended value is
    /// <c>catalog-stock-level-watcher</c>.
    /// </summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(StockLevelChangedEventConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
