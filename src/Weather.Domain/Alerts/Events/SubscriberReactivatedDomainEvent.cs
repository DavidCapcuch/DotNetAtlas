using Platform.SharedKernel.Base.DomainEvents;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a lapsed subscriber reactivates their paid subscription.
/// This represents a returning customer whose previous paid subscription had expired.
/// </summary>
public sealed record SubscriberReactivatedDomainEvent : DomainEvent
{
    /// <summary>
    /// Identifier of the subscriber aggregate.
    /// </summary>
    public required Guid SubscriberId { get; init; }

    /// <summary>
    /// User identifier (for notification lookup).
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Correlation ID for saga workflow tracking.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Payment transaction ID for saga correlation.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Subscription tier activated.
    /// </summary>
    public required SubscriptionTier Tier { get; init; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    public required int DurationDays { get; init; }

    /// <summary>
    /// When the new subscription expires.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>
    /// When the previous paid subscription expired.
    /// Used for personalized "welcome back" messaging (e.g., "It's been X months since...").
    /// </summary>
    public required DateTimeOffset PreviousSubscriptionExpiredAtUtc { get; init; }
}
