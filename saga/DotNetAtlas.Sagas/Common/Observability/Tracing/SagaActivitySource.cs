using System.Diagnostics;

namespace DotNetAtlas.Sagas.Common.Observability.Tracing;

/// <summary>
/// Shared OpenTelemetry instrumentation for saga activities.
/// Provides ActivitySource for tracing that is shared across all saga types.
/// </summary>
public static class SagaActivitySource
{
    /// <summary>
    /// Meter name for OpenTelemetry metrics configuration.
    /// Used by saga-specific instrumentation classes.
    /// </summary>
    public const string MeterName = ApplicationInfo.AppName;

    /// <summary>
    /// ActivitySource name for OpenTelemetry tracing configuration.
    /// </summary>
    public const string ActivitySourceName = ApplicationInfo.AppName;

    /// <summary>
    /// ActivitySource for distributed tracing of saga activities.
    /// Shared across all saga types.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ApplicationInfo.Version);

    /// <summary>
    /// Creates a new activity for a saga operation.
    /// </summary>
    /// <param name="operationName">Name of the operation being traced.</param>
    /// <param name="correlationId">The saga correlation ID.</param>
    /// <returns>The created activity, or null if tracing is disabled.</returns>
    public static Activity? StartActivity(string operationName, Guid correlationId)
    {
        var activity = ActivitySource.StartActivity(operationName);
        activity?.SetTag(SagaActivityTags.CorrelationId, correlationId.ToString());
        return activity;
    }
}
