using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts.ValueObjects;

namespace DotNetAtlas.Application.WeatherAlerts.PurchaseSubscription;

/// <summary>
/// Command to process a subscription purchase based on a Kafka event.
/// </summary>
public class PurchaseSubscriptionCommand : ICommand
{
    /// <summary>
    /// User who purchased the subscription.
    /// </summary>
    public required Guid UserId { get; set; }

    /// <summary>
    /// Correlation ID for saga workflow tracking.
    /// </summary>
    public required Guid CorrelationId { get; set; }

    /// <summary>
    /// Payment transaction ID for saga correlation.
    /// Used to correlate activation success/failure events with the original purchase.
    /// </summary>
    public required Guid PaymentTransactionId { get; set; }

    /// <summary>
    /// Subscription tier purchased.
    /// </summary>
    public required SubscriptionTier Tier { get; set; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    public required int DurationDays { get; set; }

    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public required DateTimeOffset OccurredOnUtc { get; set; }
}
