using OpenTelemetry.Trace;

namespace Platform.ServiceDefaults.Pii;

/// <summary>
/// Extension methods for wiring the platform's PII processors onto an
/// OpenTelemetry tracer pipeline (ADR-0011).
/// </summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="OtelPiiActivityProcessor"/> as the last processor on
    /// the tracer pipeline. Place this call AFTER any instrumentation /
    /// enrichment registrations so the redaction runs immediately before export
    /// — earlier processors may legitimately need to read the PII-tagged values
    /// for branching, but anything downstream of this point sees only redacted
    /// values.
    /// </summary>
    public static TracerProviderBuilder AddPiiRedactionProcessor(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddProcessor(new OtelPiiActivityProcessor());
    }
}
