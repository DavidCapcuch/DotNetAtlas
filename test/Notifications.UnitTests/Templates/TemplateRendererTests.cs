using AwesomeAssertions;
using Notifications.Domain.Templates;
using Xunit;

namespace Notifications.UnitTests.Templates;

public sealed class TemplateRendererTests
{
    [Fact]
    public void Render_ReplacesKnownToken_WithPayloadValue()
    {
        var payload = new Dictionary<string, string> { ["InvoiceNumber"] = "INV-2026-000142" };

        var result = TemplateRenderer.Render("Invoice {{InvoiceNumber}} is ready", payload);

        result.Should().Be("Invoice INV-2026-000142 is ready");
    }

    [Fact]
    public void Render_LeavesUnknownToken_Literal()
    {
        // Pinned contract: a token with no payload entry stays verbatim (most debuggable).
        var result = TemplateRenderer.Render("Hello {{Missing}}!", new Dictionary<string, string>());

        result.Should().Be("Hello {{Missing}}!");
    }

    [Fact]
    public void Render_WithNoTokens_ReturnsTemplateUnchanged()
    {
        const string template = "A plain line with no placeholders.";

        var result = TemplateRenderer.Render(
            template,
            new Dictionary<string, string> { ["Unused"] = "x" });

        result.Should().Be(template);
    }

    [Fact]
    public void Render_ToleratesInnerWhitespaceInTokens()
    {
        var payload = new Dictionary<string, string> { ["InvoiceNumber"] = "INV-2026-000142" };

        var result = TemplateRenderer.Render("Invoice {{ InvoiceNumber }} is ready", payload);

        result.Should().Be("Invoice INV-2026-000142 is ready");
    }

    [Fact]
    public void Render_ReplacesMultipleAndRepeatedTokens()
    {
        var payload = new Dictionary<string, string>
        {
            ["Name"] = "Ada",
            ["OrderNumber"] = "SO-42",
        };

        var result = TemplateRenderer.Render(
            "Hi {{Name}}, order {{OrderNumber}} shipped. Thanks, {{Name}}.",
            payload);

        result.Should().Be("Hi Ada, order SO-42 shipped. Thanks, Ada.");
    }

    [Fact]
    public void FindUnresolvedTokens_ReturnsTheDistinctKeysLeftLiteral()
    {
        var rendered = TemplateRenderer.Render(
            "Hi {{Name}}, invoice {{InvoiceNumber}} total {{TotalAmount}} ({{InvoiceNumber}})",
            new Dictionary<string, string> { ["Name"] = "Ada" });

        TemplateRenderer.FindUnresolvedTokens(rendered)
            .Should().BeEquivalentTo(["InvoiceNumber", "TotalAmount"]);
    }

    [Fact]
    public void FindUnresolvedTokens_FullyRenderedText_ReturnsEmpty()
    {
        var rendered = TemplateRenderer.Render(
            "Invoice {{InvoiceNumber}} is ready",
            new Dictionary<string, string> { ["InvoiceNumber"] = "INV-1" });

        TemplateRenderer.FindUnresolvedTokens(rendered).Should().BeEmpty();
    }
}
