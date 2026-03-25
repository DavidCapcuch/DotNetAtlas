using Ardalis.Specification;

namespace Weather.Domain.Alerts.Specifications;

/// <summary>
/// Specification to find alert subscribers who are subscribed to a specific monitored location.
/// </summary>
public sealed class AlertSubscribersByMonitoredLocationIdSpec : Specification<AlertSubscriber>
{
    public AlertSubscribersByMonitoredLocationIdSpec(Guid monitoredLocationId)
    {
        Query
            .Where(s => s.MonitoredLocationAlertsSubscriptions.Any(sub =>
                sub.MonitoredLocationId == monitoredLocationId))
            .TagWith(nameof(AlertSubscribersByMonitoredLocationIdSpec));
    }
}
