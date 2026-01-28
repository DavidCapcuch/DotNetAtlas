using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.SharedKernel.Base.DomainEvents;

namespace DotNetAtlas.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a subscriber upgrades to a paid tier.
/// </summary>
public sealed record SubscriptionUpgradedDomainEvent : DomainEvent
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
    /// Previous subscription tier before upgrade.
    /// </summary>
    public required SubscriptionTier PreviousTier { get; init; }

    /// <summary>
    /// New subscription tier after upgrade.
    /// </summary>
    public required SubscriptionTier NewTier { get; init; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    public required int DurationDays { get; init; }

    /// <summary>
    /// When the new subscription expires.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
