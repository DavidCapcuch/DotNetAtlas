using System.ComponentModel.DataAnnotations;

namespace Ordering.Application.Common.Messaging;

/// <summary>
/// Kafka topic names the Ordering Application layer emits to via the outbox.
/// Bound from configuration section <c>Topics</c> on startup; validated
/// eagerly via <c>AddOptionsWithValidateOnStart</c>.
/// </summary>
/// <remarks>
/// Ordering publishes only to <c>ordering.orders</c> (infinite retention per
/// events-catalog.md § 5.3). Saga-command topic <c>ordering.order-commands</c>
/// is consumer-side only — it is not listed here because Application does
/// not emit to it.
/// </remarks>
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
    /// Suffix appended to topic names to create Dead Letter Topics
    /// (e.g. <c>.DLT</c>). Consumed by the Kafka consumer DLT middleware.
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
