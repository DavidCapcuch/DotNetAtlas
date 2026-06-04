using System.ComponentModel.DataAnnotations;

namespace Notifications.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for the Notifications BC — the inbound channel-agnostic command topic consumed
/// by this BC (<see cref="NotifyCommands"/>) and the outbound per-channel delivery topic emitted by
/// the outbox (<see cref="NotifyEvents"/>). Bound from configuration section <see cref="Section"/>.
/// See ADR-0031.
/// </summary>
public sealed class TopicsOptions
{
    public const string Section = "Topics";

    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Suffix appended to the consumed topic to form its Dead Letter Topic (e.g., ".Notifications.DLT").
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }

    /// <summary>Topic carrying NotifyUserCommand (consumed by this BC).</summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string NotifyCommands { get; set; }

    /// <summary>Topic carrying NotificationDeliveryStatusChangedEvent (produced by this BC).</summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string NotifyEvents { get; set; }
}
