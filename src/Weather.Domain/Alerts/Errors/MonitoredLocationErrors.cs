using Platform.SharedKernel.Errors;

namespace Weather.Domain.Alerts.Errors;

public static class MonitoredLocationErrors
{
    public static NotFoundError MonitoredLocationNotFound(Guid monitoredLocationId)
        => new(nameof(MonitoredLocation), monitoredLocationId, "MonitoredLocation.NotFound");
}
