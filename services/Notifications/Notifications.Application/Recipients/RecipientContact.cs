namespace Notifications.Application.Recipients;

/// <summary>A recipient's resolved contact details. Email only in #312; phone/timezone arrive with #314.</summary>
public sealed record RecipientContact(string Email);
