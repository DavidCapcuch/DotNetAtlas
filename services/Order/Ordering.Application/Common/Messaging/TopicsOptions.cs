using System.ComponentModel.DataAnnotations;

namespace Ordering.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for outbox publishing.
/// Used by order endpoints to specify the target Kafka topic for order initiation events.
/// </summary>
public sealed class TopicsOptions
{
    public const string Section = "Topics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for Order Alert Subscription events (e.g., purchase/extension initiated).
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string OrderAlertSubscriptions { get; set; }

    /// <summary>
    /// Suffix appended to topic names to create Dead Letter Topics (e.g., ".DLT").
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
