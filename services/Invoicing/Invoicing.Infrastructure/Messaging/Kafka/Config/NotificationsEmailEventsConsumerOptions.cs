using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Invoicing.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka consumer configuration for the inbound <c>notifications.email-events</c> topic.
/// Invoicing subscribes to this generic event stream and filters by <c>TemplateId</c>
/// prefix <c>"invoicing."</c> — see <c>EmailNotificationSentEventKafkaHandler</c>.
/// </summary>
public sealed class NotificationsEmailEventsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaNotificationsEmailEventsConsumer";

    /// <summary>
    /// Consumer group id. Per the one-group-per-service rule in
    /// <c>events-catalog.md § 3.1</c>, this is <c>invoicing-group</c> — the sole
    /// Invoicing consumer group across every topic Invoicing subscribes to.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
