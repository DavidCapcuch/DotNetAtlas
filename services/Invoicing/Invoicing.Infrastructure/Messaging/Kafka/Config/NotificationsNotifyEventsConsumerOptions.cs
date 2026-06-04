using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Invoicing.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka consumer configuration for the inbound <c>TopicsOptions.NotificationsNotifyEvents</c>
/// topic. Invoicing subscribes to this generic delivery-event stream and filters to email +
/// Dispatched + the <c>TemplateKey</c> prefix <c>"invoicing."</c> — see
/// <c>NotificationDeliveryStatusChangedEventKafkaHandler</c> (ADR-0031).
/// </summary>
public sealed class NotificationsNotifyEventsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaNotificationsNotifyEventsConsumer";

    /// <summary>
    /// Consumer group id. Per the one-group-per-service rule in
    /// <c>events-catalog.md § 3.1</c>, this is <c>invoicing-group</c> — the sole
    /// Invoicing consumer group across every topic Invoicing subscribes to.
    /// </summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(NotificationsNotifyEventsConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
