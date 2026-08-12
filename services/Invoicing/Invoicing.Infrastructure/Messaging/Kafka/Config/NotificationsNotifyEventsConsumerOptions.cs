using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Invoicing.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka consumer configuration for the inbound <c>TopicsOptions.NotificationsNotifyEvents</c>
/// topic. Invoicing subscribes to this generic delivery-event stream and filters to email +
/// Dispatched + the <c>TemplateKey</c> prefix <c>"invoicing."</c> — see
/// <c>NotificationDeliveryStatusChangedEventKafkaHandler</c> (ADR-0031).
/// </summary>
/// <remarks>
/// Every librdkafka setting is bound by its <see cref="ConsumerConfig"/> property name rather than
/// being redeclared: KafkaFlow's <c>WithConsumerConfig</c> types its parameter as the base and reads
/// the base string dictionary, so a <c>new</c> redeclaration would write a CLR backing field it
/// never looks at. The reflection binder populates the shadow and the hidden base property alike, so
/// the values do still arrive — until a binder that reads only declared members (the
/// configuration-binding source generator, trimming, AOT) leaves that dictionary empty.
/// <para>
/// The consumer group id — per the one-group-per-service rule in <c>events-catalog.md § 3.1</c>,
/// <c>invoicing-group</c>, the sole Invoicing consumer group across every topic Invoicing subscribes
/// to — carries no annotation and no validator: <c>AddKafka</c> builds the cluster during DI
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
public sealed class NotificationsNotifyEventsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaNotificationsNotifyEventsConsumer";

    public int BufferSize { get; set; }

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; }
}
