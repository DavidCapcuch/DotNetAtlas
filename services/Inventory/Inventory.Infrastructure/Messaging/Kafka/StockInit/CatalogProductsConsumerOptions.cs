using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Inventory.Infrastructure.Messaging.Kafka.StockInit;

/// <summary>
/// Kafka consumer configuration for Catalog's <c>catalog.products</c> topic.
/// Inventory subscribes to learn about new products and initialize a fresh
/// event-sourced stream per <c>ProductId</c>. Bound from the
/// <c>KafkaCatalogProductsConsumer</c> section.
/// </summary>
/// <remarks>
/// <para>
/// Group id is <c>inventory-stock-init</c> — shared with
/// <see cref="OrderingOrdersConsumerOptions"/> per the
/// <c>events-catalog.md:96</c> + accepted deviation #1 in
/// <c>docs/implementation-prompts/inventory.md</c>'s wave-progress.
/// </para>
/// <para>
/// Reused-group rationale: both upstreams seed the same in-process pipeline
/// (Inventory's stream-init / release-on-cancel handler chain), and offset
/// coupling is acceptable for v1 — splitting the group is an operational
/// follow-up if replay-of-one-without-the-other ever becomes necessary.
/// </para>
/// </remarks>
public sealed class CatalogProductsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaCatalogProductsConsumer";

    /// <summary>Catalog's products topic (3 partitions).</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>
    /// Shared consumer group id with the Ordering-orders consumer
    /// (<c>inventory-stock-init</c>).
    /// </summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(CatalogProductsConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
