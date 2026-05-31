using System.ComponentModel.DataAnnotations;

namespace Catalog.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for Catalog outbox publishing AND for the inbound
/// <c>StockLevelChangedEvent</c> consumer. Bound to configuration section
/// <c>CatalogTopics</c>.
/// </summary>
public sealed class TopicsOptions
{
    public const string Section = "Topics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for product lifecycle events (infinite retention).
    /// Published by Catalog: <c>ProductCreatedEvent</c>, <c>ProductPriceChangedEvent</c>,
    /// <c>ProductDiscontinuedEvent</c>.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string CatalogProducts { get; set; }

    /// <summary>
    /// Topic for category lifecycle events (infinite retention).
    /// Published by Catalog: <c>CategoryCreatedEvent</c>.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string CatalogCategories { get; set; }

    /// <summary>
    /// Inbound topic for <c>Inventory.Stock.StockLevelChangedEvent</c>. Owned by Inventory;
    /// Catalog consumes via the <c>catalog-group</c> consumer group (one-group-per-service
    /// per <c>events-catalog.md § 3.1</c>).
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string StockLevelChangedEvent { get; set; }

    /// <summary>
    /// Suffix appended to a topic name for its dead-letter sibling (KafkaFlow DLT
    /// middleware convention). E.g. <c>".DLT"</c> turns <c>catalog.products</c> into
    /// <c>catalog.products.DLT</c>.
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
