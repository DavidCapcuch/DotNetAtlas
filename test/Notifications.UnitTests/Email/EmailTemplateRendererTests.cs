using AwesomeAssertions;
using FluentResults.Extensions.FluentAssertions;
using Notifications.Email;
using Xunit;

namespace Notifications.UnitTests.Email;

public sealed class EmailTemplateRendererTests
{
    private readonly EmailTemplateRenderer _renderer = new();

    [Fact]
    public void Render_InvoicingInvoiceDelivered_WithAllFields_ReturnsOk()
    {
        var data = new Dictionary<string, string>
        {
            ["InvoiceNumber"] = "INV-2026-000142",
            ["TotalAmount"] = "152.00",
            ["Currency"] = "EUR",
            ["ViewInvoiceUrl"] = "https://invoicing.example.com/invoices/00000000-0000-0000-0000-000000000001",
        };

        var result = _renderer.Render(toUserId: "00000000-0000-0000-0000-000000000099",
            templateId: "invoicing.invoice-delivered",
            data: data);

        result.Should().BeSuccess();
        result.Value.ToUserId.Should().Be("00000000-0000-0000-0000-000000000099");
        result.Value.Subject.Should().Be("Invoice INV-2026-000142 — your copy is ready");
        result.Value.Body.Should().Contain("INV-2026-000142");
        result.Value.Body.Should().Contain("https://invoicing.example.com/invoices/00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void Render_InvoicingInvoiceDelivered_MissingInvoiceNumber_Fails()
    {
        var data = new Dictionary<string, string>
        {
            ["ViewInvoiceUrl"] = "https://invoicing.example.com/invoices/abc",
        };

        var result = _renderer.Render("user", "invoicing.invoice-delivered", data);
        result.Should().BeFailure();
        result.Errors.Should().Contain(e => e.Message.Contains("InvoiceNumber"));
    }

    [Fact]
    public void Render_InvoicingInvoiceDelivered_MissingViewInvoiceUrl_Fails()
    {
        var data = new Dictionary<string, string> { ["InvoiceNumber"] = "INV-2026-000001" };
        var result = _renderer.Render("user", "invoicing.invoice-delivered", data);
        result.Should().BeFailure();
        result.Errors.Should().Contain(e => e.Message.Contains("ViewInvoiceUrl"));
    }

    [Fact]
    public void Render_UnknownTemplate_Fails()
    {
        var result = _renderer.Render("user", "unknown.template", new Dictionary<string, string>());
        result.Should().BeFailure();
    }
}
