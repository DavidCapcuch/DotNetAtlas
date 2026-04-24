using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Ordering.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Kafka consumer configuration for the <c>ordering.order-commands</c>
/// saga-command topic. Bound from <c>KafkaOrderCommandsConsumer</c> section.
/// Inherits from <see cref="ConsumerConfig"/> so broker-level knobs
/// (auto-offset-reset, session-timeout, etc.) are bindable directly.
/// </summary>
public sealed class OrderCommandsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaOrderCommandsConsumer";

    /// <summary>Topic carrying saga-issued commands to Ordering.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>Consumer group id for this consumer (idempotent rebalance key).</summary>
    [Required(ErrorMessage = $"{nameof(GroupId)} for {nameof(OrderCommandsConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
