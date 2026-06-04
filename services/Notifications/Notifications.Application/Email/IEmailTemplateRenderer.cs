using FluentResults;

namespace Notifications.Application.Email;

/// <summary>Renders a template + payload into an <see cref="EmailMessage"/>. Minimal token-replace in
/// #312 (one inline template); a template store + engine is a later slice. See ADR-0032 § 7.</summary>
public interface IEmailTemplateRenderer
{
    Result<EmailMessage> Render(string to, string templateKey, IDictionary<string, string> data);
}
