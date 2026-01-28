using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.SharedKernel.Base.DomainEvents;

namespace DotNetAtlas.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a subscriber's paid subscription expires and is downgraded to free tier.
/// </summary>
public sealed record SubscriptionDowngradedDomainEvent : DomainEvent
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
    /// Previous subscription tier before downgrade.
    /// </summary>
    public required SubscriptionTier PreviousTier { get; init; }

    /// <summary>
    /// When the subscription expired (causing the downgrade).
    /// </summary>
    public required DateTimeOffset ExpiredAtUtc { get; init; }

    /// <summary>
    /// Number of location subscriptions removed due to free tier limit.
    /// </summary>
    public required int SubscriptionsRemoved { get; init; }
}
