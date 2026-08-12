using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Catalog.Infrastructure.Messaging.Kafka.StockEvents;

/// <summary>
/// Kafka consumer configuration for the inbound <c>TopicsOptions.InventoryStockEvents</c>
/// topic. Bound from <c>KafkaInventoryStockEventsConsumer</c>. Inherits from
/// <see cref="ConsumerConfig"/> so broker-level knobs (auto-offset-reset, session-timeout)
/// are bindable directly.
/// </summary>
/// <remarks>
/// Every librdkafka setting is bound by its <see cref="ConsumerConfig"/> property name rather than
/// being redeclared: KafkaFlow's <c>WithConsumerConfig</c> types its parameter as the base and reads
/// the base string dictionary, so a <c>new</c> redeclaration would write a CLR backing field it
/// never looks at. The reflection binder populates the shadow and the hidden base property alike, so
/// the values do still arrive — until a binder that reads only declared members (the
/// configuration-binding source generator, trimming, AOT) leaves that dictionary empty.
/// <para>
/// Consumer group id: per the one-group-per-service rule in <c>events-catalog.md § 3.1</c>, this is
/// <c>catalog-group</c> — the sole Catalog consumer group across every topic Catalog subscribes to.
/// It carries no annotation and no validator: <c>AddKafka</c> builds the cluster during DI
/// registration, and <c>ConsumerConfiguration</c>'s constructor rejects a null or empty
/// <c>GroupId</c> there — strictly before options validation, which only runs once the host starts.
/// </para>
/// <para>
/// <see cref="BufferSize"/> and <see cref="WorkersCount"/> are KafkaFlow's own knobs, not librdkafka
/// settings. Only <see cref="WorkersCount"/> is annotated: KafkaFlow rejects a non-positive buffer
/// size during that same registration, whereas it accepts <c>0</c> workers and would run a consumer
/// that silently consumes nothing — so that range check is the one guard here with anything left to
/// catch.
/// </para>
/// </remarks>
public sealed class InventoryStockEventsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaInventoryStockEventsConsumer";

    public int BufferSize { get; set; }

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; }
}
