using DotNetAtlas.SharedKernel.Base;

namespace DotNetAtlas.Domain.Alerts.Entities;

/// <summary>
/// Entity representing a user's subscription to weather alerts for a specific monitored location.
/// Owned by the AlertSubscriber aggregate.
/// References MonitoredLocation by ID only (no navigation property) to maintain aggregate boundaries.
/// </summary>
public sealed class MonitoredLocationAlertsSubscription : Entity<Guid>, IAuditableEntity
{
    /// <summary>
    /// ID reference to the MonitoredLocation aggregate.
    /// No navigation property - aggregates are referenced by ID only.
    /// </summary>
    public Guid MonitoredLocationId { get; private set; }

    private MonitoredLocationAlertsSubscription()
    {
    }

    internal static MonitoredLocationAlertsSubscription Create(Guid monitoredLocationId)
    {
        return new MonitoredLocationAlertsSubscription
        {
            Id = Guid.CreateVersion7(),
            MonitoredLocationId = monitoredLocationId
        };
    }

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset LastModifiedUtc { get; private set; }
}
