using System.ComponentModel.DataAnnotations;

namespace Catalog.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for Catalog outbox publishing.
/// Bound to configuration section <c>CatalogTopics</c> and consumed by outbox publishers.
/// </summary>
public sealed class CatalogTopicsOptions
{
    public const string Section = "CatalogTopics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for product lifecycle events (infinite retention).
    /// Published by Catalog: <c>ProductCreatedEvent</c>, <c>ProductPriceChanged</c>,
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
    /// Suffix appended to topic names to create Dead Letter Topics (e.g., ".DLT").
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
