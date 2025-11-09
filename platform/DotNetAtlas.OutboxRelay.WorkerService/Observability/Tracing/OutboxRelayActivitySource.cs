using System.Diagnostics;

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.Tracing;

public static class OutboxRelayActivitySource
{
    /// <summary>
    /// Gets the activity source for OutboxRelay operations.
    /// </summary>
    public static ActivitySource ActivitySource { get; }
        = new(OutboxRelayInstrumentation.AppName, OutboxRelayInstrumentation.Version);
}
