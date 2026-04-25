using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Inventory.Infrastructure.Messaging.Kafka.StockInit;

/// <summary>
/// Kafka consumer configuration for Ordering's <c>ordering.orders</c> topic.
/// Inventory subscribes to <c>OrderCancelledEvent</c> messages so it can
/// release any still-Active reservations associated with the order ("release
/// if still reserved"). Bound from the <c>KafkaOrderingOrdersConsumer</c>
/// section.
/// </summary>
/// <remarks>
/// Group id is <c>inventory-stock-init</c> — shared with
/// <see cref="CatalogProductsConsumerOptions"/> per the
/// <c>events-catalog.md:96</c> + accepted deviation #1 in
/// <c>docs/implementation-prompts/inventory.md</c>'s wave-progress.
/// </remarks>
public sealed class OrderingOrdersConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaOrderingOrdersConsumer";

    /// <summary>Ordering's orders topic.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>
    /// Shared consumer group id with the Catalog-products consumer
    /// (<c>inventory-stock-init</c>).
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
