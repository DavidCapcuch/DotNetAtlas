using System.ComponentModel.DataAnnotations;

namespace Ordering.Application.Common.Messaging;

/// <summary>
/// Kafka topic names owned by the Ordering bounded context — both outbound
/// (published via the outbox) and inbound (consumed by the saga-command
/// consumer). Bound from configuration section <c>Topics</c> on startup;
/// validated eagerly via <c>AddOptionsWithValidateOnStart</c>.
/// </summary>
public sealed class TopicsOptions
{
    public const string Section = "Topics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic carrying external Ordering events — <c>OrderCreatedEvent</c>,
    /// <c>OrderConfirmedEvent</c>, <c>OrderCancelledEvent</c>,
    /// <c>OrderShippedEvent</c>, <c>OrderDeliveredEvent</c>,
    /// <c>OrderFailedEvent</c>. Single topic per events-catalog.md § 5.3.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string OrderingOrders { get; set; }

    /// <summary>
    /// Inbound saga-command topic — owned by Ordering. Carries
    /// <c>CreateOrderCommand</c> / <c>ConfirmOrderCommand</c> / <c>CancelOrderCommand</c> /
    /// <c>MarkOrderFailedCommand</c>. Saga is the producer; Ordering is the consumer.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string OrderCommands { get; set; }

    /// <summary>
    /// Suffix appended to topic names to create Dead Letter Topics
    /// (e.g. <c>.DLT</c>). Consumed by the Kafka consumer DLT middleware.
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
