using Platform.SharedKernel.Base.DomainEvents;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a subscriber activates their first paid subscription.
/// This represents a brand new paying customer who has never had a paid subscription before.
/// </summary>
public sealed record SubscriberActivatedDomainEvent : DomainEvent
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
}
