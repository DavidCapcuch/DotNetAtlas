using System.ComponentModel.DataAnnotations;

namespace Notifications.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for the Notifications BC — the inbound command topic consumed by the
/// <c>KafkaEmailCommandsConsumer</c> (<see cref="EmailCommands"/>) and the outbound
/// notification-lifecycle topic emitted by the outbox (<see cref="EmailEvents"/>).
/// Bound from configuration section <see cref="Section"/>.
/// </summary>
public sealed class TopicsOptions
{
    public const string Section = "Topics";

    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Suffix appended to topic names to create Dead Letter Topics (e.g., ".DLT").
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }

    /// <summary>Topic carrying SendEmailNotificationCommand (consumed by this BC).</summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string EmailCommands { get; set; }

    /// <summary>Topic carrying EmailNotificationSentEvent (produced by this BC).</summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string EmailEvents { get; set; }
}
