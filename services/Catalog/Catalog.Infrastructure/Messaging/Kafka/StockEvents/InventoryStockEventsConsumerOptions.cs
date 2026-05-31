using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Catalog.Infrastructure.Messaging.Kafka.StockEvents;

/// <summary>
/// Kafka consumer configuration for the inbound <c>TopicsOptions.InventoryStockEvents</c>
/// topic. Bound from <c>KafkaInventoryStockEventsConsumer</c>. Inherits from
/// <see cref="ConsumerConfig"/> so broker-level knobs (auto-offset-reset, session-timeout)
/// are bindable directly.
/// </summary>
public sealed class InventoryStockEventsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaInventoryStockEventsConsumer";

    /// <summary>
    /// Consumer group id. Per the one-group-per-service rule in
    /// <c>events-catalog.md § 3.1</c>, this is <c>catalog-group</c> — the sole
    /// Catalog consumer group across every topic Catalog subscribes to.
    /// </summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(InventoryStockEventsConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
