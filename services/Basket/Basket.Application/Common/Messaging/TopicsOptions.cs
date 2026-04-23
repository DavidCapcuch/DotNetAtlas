using System.ComponentModel.DataAnnotations;

namespace Basket.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for the Basket bounded context's outbox publishing.
/// </summary>
/// <remarks>
/// Bound from configuration section <see cref="Section"/>. The single topic owned by
/// Basket is <c>basket.sessions</c> (see <see cref="BasketSessions"/> and
/// <c>events-catalog.md § 5.2</c>).
/// </remarks>
public sealed class TopicsOptions
{
    /// <summary>Configuration section name — matches Weather convention.</summary>
    public const string Section = "Topics";

    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for Basket checkout-initiated events. Consumed by the Checkout saga
    /// (<c>saga/SagaOrchestrators/Checkout/</c>). Default in appsettings: <c>basket.sessions</c>.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string BasketSessions { get; set; }

    /// <summary>
    /// Suffix appended to topic names to produce Dead Letter Topic names (e.g., <c>.DLT</c>).
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
