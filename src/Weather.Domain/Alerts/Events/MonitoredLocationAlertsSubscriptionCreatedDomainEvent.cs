using Platform.SharedKernel.Base.DomainEvents;

namespace Weather.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a user subscribes to weather alerts for a monitored location.
/// </summary>
public sealed record MonitoredLocationAlertsSubscriptionCreatedDomainEvent : DomainEvent
{
    /// <summary>
    /// Identifier of the subscription entity.
    /// </summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>
    /// Identifier of the monitored location being subscribed to.
    /// </summary>
    public required Guid MonitoredLocationId { get; init; }

    /// <summary>
    /// User who subscribed.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Total subscription count for the user after this subscription.
    /// </summary>
    public required int CurrentSubscriptions { get; init; }
}
