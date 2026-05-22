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

    /// <summary>Inbound topic — owned by the Notifications bounded context.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>Consumer group id. Recommended value: <c>invoicing-notifications-email</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
