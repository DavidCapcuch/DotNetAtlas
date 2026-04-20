namespace Platform.KafkaFlow.ProducerHeaders;

/// <summary>
/// Internal mirror of the cross-transport correlation-id identifiers published by
/// <c>Platform.ServiceDefaults.CorrelationId.CorrelationIdContextKeys</c>.
/// </summary>
/// <remarks>
/// These constants are intentionally duplicated here — <c>Platform.KafkaFlow.ProducerHeaders</c>
/// does <em>not</em> take a project reference on <c>Platform.ServiceDefaults</c> because that
/// would force every consumer of Kafka producer / consumer middleware to also bring in the entire
/// HTTP-edge stack (Serilog, OpenTelemetry, JwtBearer, OpenFeature, output caching, ASP.NET).
/// The dual-project unit tests assert that the two sets of constants agree at runtime; any divergence
/// is caught there.
/// </remarks>
internal static class CorrelationIdKeys
{
    /// <summary>
    /// OpenTelemetry <see cref="System.Diagnostics.Activity"/> tag name carrying the correlation id.
    /// Mirrors <c>CorrelationIdContextKeys.ActivityTagName</c>.
    /// </summary>
    public const string ActivityTagName = "correlation.id";

    /// <summary>
    /// Serilog <see cref="global::Serilog.Context.LogContext"/> property name pushed around the
    /// consumer dispatch scope. Mirrors <c>CorrelationIdContextKeys.SerilogPropertyName</c>.
    /// </summary>
    public const string SerilogPropertyName = "CorrelationId";
}
