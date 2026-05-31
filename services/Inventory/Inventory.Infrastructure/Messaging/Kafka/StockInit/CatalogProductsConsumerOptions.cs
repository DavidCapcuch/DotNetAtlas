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
/// Group id is <c>inventory-group</c> — the sole Inventory consumer group across
/// every topic Inventory subscribes to. See the one-group-per-service rule in
/// <c>events-catalog.md § 3.1</c>: per-topic offsets are tracked independently
/// within the group, so a separate group per source topic is unnecessary
/// operational overhead. Kafka commits offsets per <c>(group, topic, partition)</c>;
/// shared group id across topics does not couple their offset positions.
/// </remarks>
public sealed class CatalogProductsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaCatalogProductsConsumer";

    /// <summary>Catalog's products topic (3 partitions).</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>
    /// Inventory's sole consumer group id (<c>inventory-group</c>); shared with
    /// every other Inventory Kafka consumer per <c>events-catalog.md § 3.1</c>.
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
