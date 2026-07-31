using System.Text.Json.Nodes;
using Platform.Api.UnitTests.Swagger.Probe;

namespace Platform.Api.UnitTests.Swagger;

/// <summary>
/// Pins the <c>required</c> set of the OpenAPI document <c>AddPlatformAuthSwaggerDocument</c>
/// produces (ADR-0038 § Decision). Assertions run against the <b>emitted document</b>, never against
/// the C# attribute — the attribute is already there today and proves nothing about what NJsonSchema
/// emits, which is what oasdiff and the client generator read.
/// </summary>
public class OpenApiRequiredPropertiesTests
{
    [Fact]
    public async Task AddPlatformAuthSwaggerDocument_MarksNonNullableMembersRequired_AndLeavesNullableOnesOptional()
    {
        await using var host = await ProbeApiHost.StartAsync();

        var document = await host.GetOpenApiDocumentAsync();

        var required = RequiredMembersOf(document, nameof(ContractProbeResponse));

        using (new AssertionScope())
        {
            required.Should().Contain("productId");
            required.Should().Contain(
                "sku",
                "a non-nullable reference type is decided from its NRT annotation, a different "
                + "NJsonSchema path from a value type's inherent non-nullability");
            required.Should().Contain(
                "price",
                "non-nullable-not-`required` still means present on the wire — the contract is "
                + "nullability-driven, so the C# `required` modifier is not what decides this");
            required.Should().NotContain(
                "note",
                "a nullable member may be absent, even when C# `required` forces it at construction");
        }
    }

    [Fact]
    public async Task AddPlatformAuthSwaggerDocument_AppliesTheSameRule_ToANestedSchema()
    {
        await using var host = await ProbeApiHost.StartAsync();

        var document = await host.GetOpenApiDocumentAsync();

        var required = RequiredMembersOf(document, nameof(ProbeMoney));

        using (new AssertionScope())
        {
            required.Should().Contain(
                "amount",
                "the rule must reach schemas behind a $ref — a consumer binds those members too");
            required.Should().NotContain("currency", "a nullable member stays optional at any depth");
        }
    }

    [Fact]
    public async Task AddPlatformAuthSwaggerDocument_WithoutAnAuthority_StillCarriesRequired()
    {
        await using var host = await ProbeApiHost.StartAsync(authority: null);

        var document = await host.GetOpenApiDocumentAsync();

        var required = RequiredMembersOf(document, nameof(ContractProbeResponse));

        required.Should().Contain(
            "productId",
            "a tier that drops the JwtBearer authority skips the OAuth2 scheme, but its document is "
            + "still a published contract — the required set must not ride on that branch");
    }

    /// <summary>
    /// Guards the assumption behind issue #368's AC on <c>allOf</c>: oasdiff's <c>--flatten-allof</c>
    /// defaults off, so a required set hidden inside an <c>allOf</c> branch would be invisible to it.
    /// NSwag emits a bare <c>$ref</c> for a non-nullable nested member, and no wire response type in
    /// this repo uses inheritance — the other shape that produces <c>allOf</c>. This test fails the
    /// day either premise stops holding, which is when the snapshot slices need to know.
    /// </summary>
    [Fact]
    public async Task AddPlatformAuthSwaggerDocument_EmitsNoAllOf_SoRequiredStaysVisibleToOasdiff()
    {
        await using var host = await ProbeApiHost.StartAsync();

        var document = await host.GetOpenApiDocumentJsonAsync();

        document.Should().NotContain("allOf");
    }

    private static string[] RequiredMembersOf(JsonNode document, string schemaName)
    {
        var schema = document["components"]?["schemas"]?[schemaName]
            ?? throw new InvalidOperationException(
                $"The document carries no '{schemaName}' schema. Emitted schemas: "
                + string.Join(", ", document["components"]?["schemas"]?.AsObject().Select(p => p.Key) ?? []));

        return schema["required"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray() ?? [];
    }
}
