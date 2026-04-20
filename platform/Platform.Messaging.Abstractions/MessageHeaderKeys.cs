namespace Platform.Messaging.Abstractions;

/// <summary>
/// Standard message header keys used across messaging infrastructure.
/// These headers enable cross-cutting concerns like idempotency and tracing.
/// </summary>
/// <remarks>
/// See https://developer.confluent.io/courses/event-design/best-practices/.
/// </remarks>
public static class MessageHeaderKeys
{
    /// <summary>
    /// Header key for the unique message identifier used for idempotent processing.
    /// Value should be a GUID string.
    /// </summary>
    public const string MessageId = "message.id";

    /// <summary>
    /// Header key for the origin service identifier.
    /// Identifies which service produced the message.
    /// </summary>
    public const string Origin = "origin";

    /// <summary>
    /// Header key for the business-workflow correlation identifier.
    /// Threads a single workflow (e.g., one checkout) across HTTP + Kafka + DB boundaries.
    /// Value should be a UUID v7 string. Per ADR-0008, the ASP.NET edge generates this
    /// when absent on inbound HTTP; outbox publishers copy the ambient value; Kafka producer
    /// middleware auto-generates only when originating a new workflow.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>traceparent</c> (OpenTelemetry) — correlation.id is a
    /// long-lived business identifier persisted to DB rows and event payloads;
    /// <c>traceparent</c> is ephemeral to the tracing pipeline.
    /// </remarks>
    public const string CorrelationId = "correlation.id";
}
