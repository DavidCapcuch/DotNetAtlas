using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Application.Common.Messaging;

/// <summary>
/// Kafka topic names for outbox publishing.
/// Used by domain event handlers to specify the target Kafka topic for integration events.
/// </summary>
public sealed class TopicsOptions
{
    public const string Section = "Topics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for forecast requested events.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string ForecastRequested { get; set; }

    /// <summary>
    /// Topic for Weather Alerts commands.
    /// Consumed by WeatherAlerts service for subscription management:
    /// - ActivateSubscriptionCommand (from Purchase Saga)
    /// - ExtendSubscriptionCommand (from Extension Saga).
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string WeatherAlertSubscriptionsCommands { get; set; }

    /// <summary>
    /// Topic for Weather Alerts events.
    /// Published by WeatherAlerts service:
    /// - SubscriptionActivatedEvent
    /// - SubscriptionActivationFailedEvent
    /// - SubscriptionExtendedEvent
    /// - SubscriptionExtensionActivationFailedEvent.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string WeatherAlertSubscriptions { get; set; }

    /// <summary>
    /// Topic for Order Alert Subscription events.
    /// Published by Order service:
    /// - AlertSubscriptionPurchaseInitiatedEvent
    /// - AlertSubscriptionExtensionInitiatedEvent.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string OrderAlertSubscriptions { get; set; }

    /// <summary>
    /// Topic for Notification commands.
    /// Consumed by Notification service:
    /// - SendEmailNotificationCommand.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string NotificationCommands { get; set; }

    /// <summary>
    /// Topic for Payment commands.
    /// Consumed by Payment service:
    /// - AuthorizePaymentCommand
    /// - CapturePaymentCommand
    /// - RequestRefundCommand
    /// - VoidPaymentCommand.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string PaymentCommands { get; set; }

    /// <summary>
    /// Topic for Payment events.
    /// Published by Payment service:
    /// - PaymentAuthorizationFailedEvent
    /// - PaymentAuthorizedEvent
    /// - PaymentCapturedEvent
    /// - PaymentCaptureFailedEvent
    /// - PaymentCompletedEvent
    /// - PaymentFailedEvent
    /// - PaymentRefundedEvent
    /// - PaymentRequestedEvent
    /// - PaymentVoidedEvent.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string Payments { get; set; }

    /// <summary>
    /// Topic for weather feedback events.
    /// Published by WeatherAlerts service for feedback tracking.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string WeatherFeedbackEvents { get; set; }

    /// <summary>
    /// Suffix appended to topic names to create Dead Letter Topics (e.g., ".DLT").
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
