namespace DotNetAtlas.Messaging.Abstractions;

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
}
