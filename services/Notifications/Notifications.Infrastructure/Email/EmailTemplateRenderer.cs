using FluentResults;
using Notifications.Application.Email;

namespace Notifications.Infrastructure.Email;

/// <summary>Phase-1 in-process renderer. One hardcoded template (<c>invoicing.invoice-delivered</c>);
/// future Phase-2 work introduces a template store + Razor/Liquid.</summary>
internal sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public Result<EmailMessage> Render(string toUserId, string templateId, IDictionary<string, string> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(data);

        return templateId switch
        {
            "invoicing.invoice-delivered" => RenderInvoiceDelivered(toUserId, data),
            _ => Result.Fail<EmailMessage>($"Unknown template '{templateId}'."),
        };
    }

    private static Result<EmailMessage> RenderInvoiceDelivered(string toUserId, IDictionary<string, string> d)
    {
        if (!d.TryGetValue("InvoiceNumber", out var num) || string.IsNullOrWhiteSpace(num))
        {
            return Result.Fail<EmailMessage>("Missing 'InvoiceNumber'.");
        }

        if (!d.TryGetValue("ViewInvoiceUrl", out var url) || string.IsNullOrWhiteSpace(url))
        {
            return Result.Fail<EmailMessage>("Missing 'ViewInvoiceUrl'.");
        }

        var subject = $"Invoice {num} — your copy is ready";
        d.TryGetValue("TotalAmount", out var total);
        d.TryGetValue("Currency", out var currency);
        total ??= "";
        currency ??= "";
        var totalLine = string.IsNullOrWhiteSpace(total) || string.IsNullOrWhiteSpace(currency)
            ? string.Empty
            : $"Total: {total} {currency}\n";

        var body = $"Hello,\n\nYour invoice {num} is ready.\n{totalLine}Sign in to view & download: {url}\n";
        return Result.Ok(new EmailMessage(toUserId, subject, body));
    }
}
