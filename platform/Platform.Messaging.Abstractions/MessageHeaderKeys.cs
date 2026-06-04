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
    /// Header key for the legacy business-workflow correlation identifier (ADR-0008), retired by
    /// ADR-0030. Nothing produces this header any more — the HTTP/Kafka middleware, the outbox
    /// header path, and the Serilog enricher were removed in Part B. The constant survives only
    /// because the Ordering/Inventory/Invoicing saga-command consumers still read it via
    /// <c>ExtractCorrelationId()</c> until their reads are retargeted onto the wire <c>OrderId</c>;
    /// it is deleted with them. Cross-process correlation is now W3C <c>traceparent</c> (OpenTelemetry).
    /// </summary>
    public const string CorrelationId = "correlation.id";
}
