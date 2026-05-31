using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Inventory.Infrastructure.Messaging.Kafka.StockInit;

/// <summary>
/// Kafka consumer configuration for Ordering's <c>TopicsOptions.OrderingOrders</c> topic.
/// Inventory subscribes to <c>OrderCancelledEvent</c> messages so it can
/// release any still-Active reservations associated with the order ("release
/// if still reserved"). Bound from the <c>KafkaOrderingOrdersConsumer</c>
/// section.
/// </summary>
/// <remarks>
/// Group id is <c>inventory-group</c> — Inventory's sole consumer group across
/// every topic it subscribes to. See <c>events-catalog.md § 3.1</c> for the
/// one-group-per-service rule. Kafka commits offsets per
/// <c>(group, topic, partition)</c>, so sharing the group id across topics does
/// not couple their offset cursors.
/// </remarks>
public sealed class OrderingOrdersConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaOrderingOrdersConsumer";

    /// <summary>
    /// Inventory's sole consumer group id (<c>inventory-group</c>); shared with
    /// every other Inventory Kafka consumer per <c>events-catalog.md § 3.1</c>.
    /// </summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(OrderingOrdersConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
