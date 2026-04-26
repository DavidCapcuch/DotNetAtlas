using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Invoicing.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka consumer configuration for the inbound <c>payments.transactions</c>
/// topic. One consumer instance subscribes; KafkaFlow's
/// <c>AddTypedHandlers</c> dispatches between
/// <c>PaymentCapturedInvoiceProjectionKafkaHandler</c> and
/// <c>PaymentRefundedCreditNoteProjectionKafkaHandler</c> based on the Avro
/// type.
/// </summary>
public sealed class PaymentsTransactionsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaPaymentsTransactionsConsumer";

    /// <summary>Inbound topic — owned by the Payments bounded context.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>
    /// Consumer group id. Recommended value is
    /// <c>invoicing-payments-projection</c>.
    /// </summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(PaymentsTransactionsConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
