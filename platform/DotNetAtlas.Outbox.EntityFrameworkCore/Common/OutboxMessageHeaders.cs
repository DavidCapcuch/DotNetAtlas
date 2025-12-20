namespace DotNetAtlas.Outbox.EntityFrameworkCore.Common;

public static class OutboxMessageHeaders
{
    /// <summary>
    /// Header key for the unique message identifier used for idempotent processing.
    /// </summary>
    public const string MessageId = "message.id";

    /// <summary>
    /// Header key for the origin service identifier.
    /// </summary>
    public const string Origin = "origin";
}
