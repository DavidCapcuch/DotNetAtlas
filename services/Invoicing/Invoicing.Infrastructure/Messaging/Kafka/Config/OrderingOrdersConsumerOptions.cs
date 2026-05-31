using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Invoicing.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka consumer configuration for the inbound <c>TopicsOptions.OrderingOrders</c> topic.
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

    /// <summary>
    /// Consumer group id. Per the one-group-per-service rule in
    /// <c>events-catalog.md § 3.1</c>, this is <c>invoicing-group</c> — the sole
    /// Invoicing consumer group across every topic Invoicing subscribes to.
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
