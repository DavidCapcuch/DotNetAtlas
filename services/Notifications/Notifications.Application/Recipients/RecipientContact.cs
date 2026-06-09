namespace Notifications.Application.Recipients;

/// <summary>
/// A recipient's resolved contact details. Email only — the v2 email path needs just the address;
/// phone + timezone (for the SMS channel and quiet-hours scheduling) arrive with #315.
/// </summary>
public sealed record RecipientContact(string Email);
