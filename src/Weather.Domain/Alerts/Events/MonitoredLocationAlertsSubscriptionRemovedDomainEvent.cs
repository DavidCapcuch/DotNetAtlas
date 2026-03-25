using Platform.SharedKernel.Base.DomainEvents;

namespace Weather.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a user unsubscribes from weather alerts for a monitored location.
/// </summary>
public sealed record MonitoredLocationAlertsSubscriptionRemovedDomainEvent : DomainEvent
{
    /// <summary>
    /// Identifier of the subscription entity that was removed.
    /// </summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>
    /// Identifier of the monitored location that was unsubscribed from.
    /// </summary>
    public required Guid MonitoredLocationId { get; init; }

    /// <summary>
    /// User who unsubscribed.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Total subscription count for the user after this unsubscription.
    /// </summary>
    public required int CurrentSubscriptions { get; init; }
}
