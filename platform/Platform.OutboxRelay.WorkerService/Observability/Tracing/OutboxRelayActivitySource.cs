using System.Diagnostics;

namespace Platform.OutboxRelay.WorkerService.Observability.Tracing;

public static class OutboxRelayActivitySource
{
    /// <summary>
    /// ActivitySource name for OpenTelemetry tracing configuration.
    /// </summary>
    public const string ActivitySourceName = ApplicationInfo.AppName;

    /// <summary>
    /// Gets the activity source for OutboxRelay operations.
    /// </summary>
    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName, ApplicationInfo.Version);
}
