using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Notifications.Infrastructure.Common.Config;

/// <summary>
/// Kafka consumer configuration for the inbound <c>TopicsOptions.NotifyCommands</c> topic.
/// Bound from <c>KafkaNotifyCommandsConsumer</c>. Inherits from <see cref="ConsumerConfig"/> so
/// broker-level knobs (auto-offset-reset, session-timeout) are bindable directly.
/// </summary>
/// <remarks>
/// Recommended read: https://github.com/confluentinc/confluent-kafka-dotnet/wiki/Consumer.
/// </remarks>
public sealed class NotifyCommandsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaNotifyCommandsConsumer";

    /// <summary>
    /// Consumer group ID for this consumer.
    /// </summary>
    [Required(ErrorMessage = $"{nameof(GroupId)} for {nameof(NotifyCommandsConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    /// <summary>
    /// Buffer size for messages.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = $"{nameof(BufferSize)} must be greater than 0")]
    public int BufferSize { get; set; } = 100;

    /// <summary>
    /// Number of workers processing messages.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = $"{nameof(WorkersCount)} must be greater than 0")]
    public int WorkersCount { get; set; } = 1;
}
