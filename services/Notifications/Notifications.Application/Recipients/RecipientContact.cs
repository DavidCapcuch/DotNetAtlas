namespace Notifications.Application.Recipients;

/// <summary>
/// A recipient's resolved contact details — the email address the email dispatcher delivers to and
/// the (fake E.164) phone number the SMS dispatcher logs to (notifications.md § 8). The recipient's
/// timezone is deliberately absent: quiet hours are evaluated at enqueue time by the Kafka handler,
/// which reads <c>user_preferences</c> directly, not by dispatchers.
/// </summary>
public sealed record RecipientContact(string Email, string PhoneNumber);
