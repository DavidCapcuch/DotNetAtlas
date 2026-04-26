using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Invoicing.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka consumer configuration for the inbound <c>ordering.orders</c> topic.
/// One consumer instance subscribes to the topic; KafkaFlow's
/// <c>AddTypedHandlers</c> dispatches between
/// <c>OrderConfirmedInvoiceProjectionKafkaHandler</c> and
/// <c>OrderCancelledCreditNoteProjectionKafkaHandler</c> based on the Avro
/// type. Inherits <see cref="ConsumerConfig"/> so broker-level knobs
/// (auto-offset-reset, session-timeout) are bindable directly.
/// </summary>
public sealed class OrderingOrdersConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaOrderingOrdersConsumer";

    /// <summary>Inbound topic — owned by the Ordering bounded context.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>
    /// Consumer group id. Per <c>events-catalog.md § 7</c> consumer groups must
    /// not collide with the producing BC's internal groups — recommended
    /// value is <c>invoicing-ordering-projection</c>.
    /// </summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(OrderingOrdersConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
