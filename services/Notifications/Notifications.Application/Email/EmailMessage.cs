namespace Notifications.Application.Email;

/// <summary>Email envelope passed to <see cref="IEmailGateway"/>. ToUserId is the
/// recipient user identity; the gateway is responsible for resolving the actual address
/// (e.g., looking up the user-profile service). Mock gateway logs without delivering.</summary>
public sealed record EmailMessage(string ToUserId, string Subject, string Body);
