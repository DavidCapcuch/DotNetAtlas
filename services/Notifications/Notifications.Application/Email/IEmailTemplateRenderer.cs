using FluentResults;

namespace Notifications.Application.Email;

public interface IEmailTemplateRenderer
{
    Result<EmailMessage> Render(string toUserId, string templateId, IDictionary<string, string> data);
}
