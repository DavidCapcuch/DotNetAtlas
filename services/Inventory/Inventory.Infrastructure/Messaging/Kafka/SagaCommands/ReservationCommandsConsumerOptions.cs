using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Inventory.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Kafka consumer configuration for the <c>TopicsOptions.InventoryReservationCommands</c>
/// saga-command topic. Bound from the <c>KafkaReservationCommandsConsumer</c> section.
/// Inherits from <see cref="ConsumerConfig"/> so broker-level knobs (auto-offset-reset,
/// session-timeout, etc.) are bindable directly.
/// </summary>
/// <remarks>
/// Carries the 3 saga commands (<c>ReserveStockCommand</c>,
/// <c>ConfirmReservationCommand</c>, <c>ReleaseReservationCommand</c>) on a
/// single 3-partition topic per <c>events-catalog.md § 3</c>. Group id is
/// <c>inventory-group</c> — Inventory's sole consumer group across every topic
/// it subscribes to (one-group-per-service rule, <c>events-catalog.md § 3.1</c>).
/// <para>
/// Every librdkafka setting is bound by its <see cref="ConsumerConfig"/> property name rather than
/// being redeclared: KafkaFlow's <c>WithConsumerConfig</c> types its parameter as the base and reads
/// the base string dictionary, so a <c>new</c> redeclaration would write a CLR backing field it
/// never looks at. The reflection binder populates the shadow and the hidden base property alike, so
/// the values do still arrive — until a binder that reads only declared members (the
/// configuration-binding source generator, trimming, AOT) leaves that dictionary empty.
/// </para>
/// <para>
/// The group id carries no annotation and no validator: <c>AddKafka</c> builds the cluster during DI
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
public sealed class ReservationCommandsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaReservationCommandsConsumer";

    public int BufferSize { get; set; }

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; }
}
