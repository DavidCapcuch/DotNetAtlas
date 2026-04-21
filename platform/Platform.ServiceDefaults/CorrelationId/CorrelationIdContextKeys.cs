namespace Platform.ServiceDefaults.CorrelationId;

/// <summary>
/// Well-known keys for correlation-id propagation across the HTTP edge (ADR-0008).
/// The Kafka-side counterpart lives in <c>Platform.Messaging.Abstractions.MessageHeaderKeys.CorrelationId</c>.
/// </summary>
public static class CorrelationIdContextKeys
{
    /// <summary>
    /// Inbound + outbound HTTP header name.
    /// </summary>
    public const string HttpHeaderName = "X-Correlation-Id";

    /// <summary>
    /// OpenTelemetry activity tag name (matches Kafka header convention for cross-transport grep-ability).
    /// The ambient correlation id is propagated via this tag on <see cref="System.Diagnostics.Activity.Current"/>;
    /// consumers — including the outbound <c>CorrelationIdDelegatingHandler</c>, Kafka producers,
    /// and background workers — read it there regardless of whether an HTTP request is in scope.
    /// </summary>
    public const string ActivityTagName = "correlation.id";

    /// <summary>
    /// Serilog <see cref="global::Serilog.Context.LogContext"/> property name pushed around the request scope.
    /// </summary>
    public const string SerilogPropertyName = "CorrelationId";
}
