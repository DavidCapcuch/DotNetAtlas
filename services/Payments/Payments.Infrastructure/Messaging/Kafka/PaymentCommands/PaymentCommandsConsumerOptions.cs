using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Kafka consumer configuration for the <c>payments.payment-commands</c> saga-command topic. Bound from
/// <c>KafkaPaymentCommandsConsumer</c> section. Inherits from <see cref="ConsumerConfig"/> so
/// broker-level knobs (auto-offset-reset, session-timeout, etc.) are bindable directly.
/// </summary>
public sealed class PaymentCommandsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaPaymentCommandsConsumer";

    /// <summary>Consumer group id for this consumer (idempotent rebalance key).</summary>
    [Required(ErrorMessage = $"{nameof(GroupId)} for {nameof(PaymentCommandsConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
