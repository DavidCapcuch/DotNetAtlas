using Platform.CQRS;

namespace Weather.Application.WeatherAlerts.ExtendSubscription;

/// <summary>
/// Command to extend a subscription.
/// </summary>
/// <remarks>
/// This is a clean application layer command. Inbox deduplication is applied
/// in the infrastructure layer (Kafka handler) where messaging concerns belong.
/// </remarks>
public class ExtendSubscriptionCommand : ICommand
{
    /// <summary>
    /// User who extended the subscription.
    /// </summary>
    public required Guid UserId { get; set; }

    /// <summary>
    /// Correlation ID for saga workflow tracking.
    /// </summary>
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// Payment transaction ID for saga correlation.
    /// Used to correlate extension success/failure events with the original extension request.
    /// </summary>
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Duration to extend the subscription in days.
    /// </summary>
    public required int DurationExtendedDays { get; set; }

    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public required DateTimeOffset OccurredOnUtc { get; set; }
}
