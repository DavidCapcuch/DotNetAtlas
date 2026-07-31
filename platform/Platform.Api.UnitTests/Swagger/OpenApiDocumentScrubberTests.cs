using System.Text.Json.Nodes;
using Platform.Api.UnitTests.Swagger.Probe;
using Platform.Test.Framework.Swagger;

namespace Platform.Api.UnitTests.Swagger;

/// <summary>
/// Pins the normalization a snapshot must apply before comparing against the committed artifact
/// (ADR-0038 § Decision). Asserted against a document the host actually generated, so the input is
/// the real emitted shape rather than a hand-written fixture that could drift from it.
/// <para>
/// Expected values are written as literals rather than read from
/// <see cref="OpenApiDocumentScrubber"/>'s constants: sourcing them from the type under test would
/// let a changed placeholder turn the scrub into a no-op with every assertion still green.
/// </para>
/// </summary>
public class OpenApiDocumentScrubberTests
{
    [Fact]
    public async Task ScrubEnvironmentDerived_ReplacesTheRequestDerivedServerUrl()
    {
        await using var host = await ProbeApiHost.StartAsync();
        var document = await host.GetOpenApiDocumentJsonAsync();

        var scrubbed = JsonNode.Parse(OpenApiDocumentScrubber.ScrubEnvironmentDerived(document))!;

        using (new AssertionScope())
        {
            ServerUrlsOf(JsonNode.Parse(document)!).Should().Equal(
                ["http://localhost"],
                "the host must really have stamped a request-derived server URL, or this proves nothing");
            ServerUrlsOf(scrubbed).Should().Equal(["/"]);
        }
    }

    [Fact]
    public async Task ScrubEnvironmentDerived_ReplacesEveryOAuth2FlowUrl()
    {
        await using var host = await ProbeApiHost.StartAsync();
        var document = await host.GetOpenApiDocumentJsonAsync();

        var scrubbedJson = OpenApiDocumentScrubber.ScrubEnvironmentDerived(document);
        var scrubbed = JsonNode.Parse(scrubbedJson)!;
        var flow = scrubbed["components"]!["securitySchemes"]!["OAuth2"]!["flows"]!["authorizationCode"]!;

        using (new AssertionScope())
        {
            document.Should().Contain(
                ProbeApiHost.Authority,
                "the flow URLs must really have been built from the configured authority");
            flow["authorizationUrl"]!.GetValue<string>().Should().Be("https://authority.invalid");
            flow["tokenUrl"]!.GetValue<string>().Should().Be("https://authority.invalid");

            // The invariant, not the two keys above: no fragment of the authority may survive
            // anywhere in the document. BuildOAuth2Scheme also sets scheme-level Swagger-2 members,
            // and a flow key absent today (refreshUrl) may appear on a future generator.
            scrubbedJson.Should().NotContain("9011").And.NotContain("realms/dotnetatlas");

            flow["scopes"]!["openid"]!.GetValue<string>().Should().Be(
                "OpenID.",
                "the advertised scope set is structure the snapshot pins, not environment-derived");
        }
    }

    [Fact]
    public async Task ScrubEnvironmentDerived_WithoutASecurityScheme_LeavesTheContractIntact()
    {
        await using var host = await ProbeApiHost.StartAsync(authority: null);
        var document = await host.GetOpenApiDocumentJsonAsync();

        var scrubbed = JsonNode.Parse(OpenApiDocumentScrubber.ScrubEnvironmentDerived(document))!;

        using (new AssertionScope())
        {
            scrubbed["components"]!["securitySchemes"].Should().BeNull(
                "a tier without an authority emits no scheme at all — the scrubber must not invent one");
            scrubbed["components"]!["schemas"]![nameof(ContractProbeResponse)]!["required"]!
                .AsArray().Select(n => n!.GetValue<string>())
                .Should().Contain("productId", "scrubbing must not disturb the contract it surrounds");
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("5")]
    [InlineData("null")]
    public void ScrubEnvironmentDerived_GivenJsonThatIsNotAnObject_Rejects(string notAnObject)
    {
        var scrub = () => OpenApiDocumentScrubber.ScrubEnvironmentDerived(notAnObject);

        scrub.Should().Throw<ArgumentException>().WithParameterName("documentJson");
    }

    private static string[] ServerUrlsOf(JsonNode document)
        => document["servers"]?.AsArray().Select(s => s!["url"]!.GetValue<string>()).ToArray() ?? [];
}
