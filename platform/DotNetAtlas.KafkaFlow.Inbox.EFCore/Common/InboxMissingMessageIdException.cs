namespace DotNetAtlas.KafkaFlow.Inbox.EFCore.Common;

/// <summary>
/// Exception thrown when an inbox message id is missing.
/// </summary>
public sealed class InboxMissingMessageIdException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxMissingMessageIdException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InboxMissingMessageIdException(string message)
        : base(message)
    {
    }

    public InboxMissingMessageIdException()
    {
    }

    public InboxMissingMessageIdException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
