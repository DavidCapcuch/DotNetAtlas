using DotNetAtlas.SharedKernel.Errors;

namespace DotNetAtlas.Domain.Alerts.Errors;

public static class MonitoredLocationErrors
{
    public static NotFoundError MonitoredLocationNotFound(Guid monitoredLocationId)
        => new(nameof(MonitoredLocation), monitoredLocationId, "MonitoredLocation.NotFound");
}
