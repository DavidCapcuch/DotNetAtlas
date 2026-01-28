using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.SharedKernel.Base.DomainEvents;

namespace DotNetAtlas.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a weather alert is issued for a monitored location.
/// This occurs when a weather reading breaches configured alert thresholds.
/// </summary>
public sealed record WeatherAlertIssuedDomainEvent : DomainEvent
{
    /// <summary>
    /// Identifier of the monitored location that issued the alert.
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

    /// <summary>
    /// The weather alert that was issued.
    /// </summary>
    public required WeatherAlert WeatherAlert { get; init; }

    /// <summary>
    /// The weather reading that triggered the alert.
    /// </summary>
    public required WeatherReading TriggeringReading { get; init; }

    /// <summary>
    /// UTC timestamp when the alert was issued.
    /// </summary>
    public required DateTimeOffset IssuedAtUtc { get; init; }
}
