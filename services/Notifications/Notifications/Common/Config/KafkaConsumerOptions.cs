using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Notifications.Common.Config;

/// <summary>
/// Kafka consumer configuration for subscription events.
/// Inherits from ConsumerConfig to expose all Confluent.Kafka consumer settings.
/// </summary>
/// <remarks>
/// Recommended read: https://github.com/confluentinc/confluent-kafka-dotnet/wiki/Consumer.
/// </remarks>
public sealed class KafkaConsumerOptions : ConsumerConfig
{
    public const string Section = $"{KafkaOptions.Section}:Consumer";

    /// <summary>
    /// Consumer group ID for this consumer.
    /// </summary>
    [Required(ErrorMessage = $"{nameof(GroupId)} for {nameof(KafkaConsumerOptions)} is missing",
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
