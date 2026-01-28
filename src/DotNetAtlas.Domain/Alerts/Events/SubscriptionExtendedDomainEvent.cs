using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.SharedKernel.Base.DomainEvents;

namespace DotNetAtlas.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a subscriber's paid subscription is extended.
/// </summary>
public sealed record SubscriptionExtendedDomainEvent : DomainEvent
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
    /// Current subscription tier.
    /// </summary>
    public required SubscriptionTier Tier { get; init; }

    /// <summary>
    /// Number of days the subscription was extended by.
    /// </summary>
    public required int ExtendedByDays { get; init; }

    /// <summary>
    /// New expiry date after extension.
    /// </summary>
    public required DateTimeOffset NewExpiresAtUtc { get; init; }
}
