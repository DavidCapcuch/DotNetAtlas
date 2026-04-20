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
    /// Key used by the middleware to stash the ambient correlation id on <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
    /// </summary>
    public const string HttpContextItemKey = "Platform.ServiceDefaults.CorrelationId";

    /// <summary>
    /// OpenTelemetry activity tag name (matches Kafka header convention for cross-transport grep-ability).
    /// </summary>
    public const string ActivityTagName = "correlation.id";

    /// <summary>
    /// Serilog <see cref="global::Serilog.Context.LogContext"/> property name pushed around the request scope.
    /// </summary>
    public const string SerilogPropertyName = "CorrelationId";
}
