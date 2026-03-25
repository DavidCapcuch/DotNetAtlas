using Platform.SharedKernel.Base.DomainEvents;
using Weather.Domain.Common.ValueObjects;

namespace Weather.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a new monitored location is created.
/// </summary>
public sealed record MonitoredLocationCreatedDomainEvent : DomainEvent
{
    /// <summary>
    /// Identifier of the newly created monitored location.
    /// </summary>
    public required Guid MonitoredLocationId { get; init; }

    /// <summary>
    /// City for the location.
    /// </summary>
    public required City City { get; init; }

    /// <summary>
    /// Country code for the location.
    /// </summary>
    public required CountryCode CountryCode { get; init; }
}
