namespace DotNetAtlas.ReliableMessaging.Inbox.Core;

/// <summary>
/// Inbox entity for idempotent message processing.
/// Tracks processed messages to prevent duplicate processing using the Inbox pattern.
/// </summary>
/// <remarks>
/// Each service should have its own inbox table in its database.
/// The MessageId alone is enough for deduplication within a service boundary.
/// </remarks>
public class InboxMessage
{
    /// <summary>
    /// The unique message identifier (Primary Key).
    /// </summary>
    public required Guid MessageId { get; set; }

    /// <summary>
    /// UTC timestamp when the message was processed.
    /// </summary>
    public required DateTimeOffset ProcessedAtUtc { get; set; }
}
