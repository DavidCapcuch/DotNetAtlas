namespace Notifications.Application.Dispatch;

/// <summary>
/// The unit of work handed to a channel dispatcher — everything one channel needs to render and
/// send a single notification. Also the serialised Hangfire job argument, so it stays a flat,
/// JSON-friendly record (no domain types).
/// </summary>
public sealed record NotificationDispatch
{
    /// <summary>Producer-assigned intent identity; keys the per-channel ledger.</summary>
    public required Guid NotificationId { get; init; }

    /// <summary>Recipient (Keycloak sub); the dispatcher resolves the channel address from it.</summary>
    public required Guid RecipientUserId { get; init; }

    /// <summary>Template identifier, e.g. <c>invoicing.invoice-delivered</c>.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>Template rendering data.</summary>
    public required Dictionary<string, string> Payload { get; init; }
}
