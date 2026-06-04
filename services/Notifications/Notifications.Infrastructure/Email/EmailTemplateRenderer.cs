using FluentResults;
using Notifications.Application.Email;

namespace Notifications.Infrastructure.Email;

/// <summary>Minimal in-process renderer for the walking skeleton (#312). One inline template
/// (<c>invoicing.invoice-delivered</c>); a template store + engine is a later slice (ADR-0032 § 7).</summary>
internal sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public Result<EmailMessage> Render(string to, string templateKey, IDictionary<string, string> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentNullException.ThrowIfNull(data);

        return templateKey switch
        {
            "invoicing.invoice-delivered" => RenderInvoiceDelivered(to, data),
            _ => Result.Fail<EmailMessage>($"Unknown template '{templateKey}'."),
        };
    }

    private static Result<EmailMessage> RenderInvoiceDelivered(string to, IDictionary<string, string> d)
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
        return Result.Ok(new EmailMessage(to, subject, body));
    }
}
