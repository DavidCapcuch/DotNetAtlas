using System.ComponentModel.DataAnnotations;

namespace Payments.Common.Config;

/// <summary>
/// Kafka topic names for outbox publishing.
/// Used by domain event handlers to specify the target Kafka topic for integration events.
/// </summary>
public sealed class TopicsOptions
{
    public const string Section = "Topics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for Payment commands.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string PaymentCommands { get; set; }

    /// <summary>
    /// Topic for Payment events.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string Payments { get; set; }

    /// <summary>
    /// Suffix appended to topic names to create Dead Letter Topics (e.g., ".DLT").
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
