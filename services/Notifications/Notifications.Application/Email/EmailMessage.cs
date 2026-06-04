namespace Notifications.Application.Email;

/// <summary>Rendered email envelope passed to <see cref="IEmailGateway"/>. <see cref="To"/> is the
/// resolved recipient email address (the dispatcher resolves it via the recipient resolver before
/// rendering). The mock gateway logs without delivering; the SMTP gateway sends to Mailpit.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);
