using System.Text.Json;
using System.Text.Json.Nodes;

namespace Platform.Test.Framework.Swagger;

/// <summary>
/// Replaces the environment-derived nodes of an OpenAPI document — <c>servers</c> and the OAuth2 flow
/// URLs — so a snapshot of it pins the contract and nothing else. See ADR-0038 § Decision for why
/// those two nodes qualify and why neither later gate would catch drift in them.
/// <para>
/// The placeholders below are part of the committed artifact of <b>every</b> producer, so changing
/// either value rewrites all of them and must land in one commit.
/// </para>
/// <para>
/// Scheme <em>presence</em> is deliberately not normalized, and is itself tier-dependent: a tier that
/// configures no authority emits no security scheme at all. Snapshots must therefore be produced and
/// compared under one configuration.
/// </para>
/// </summary>
public static class OpenApiDocumentScrubber
{
    /// <summary>
    /// Replaces the request-derived <c>servers</c> entry. Relative, so a client generated from the
    /// snapshot resolves against whatever base address its caller configures.
    /// </summary>
    public const string ServerUrlPlaceholder = "/";

    /// <summary>
    /// Replaces every OAuth2 flow URL. The whole value is authority-derived — realm segment included —
    /// so no part of it is contract. Uses the reserved <c>.invalid</c> TLD (RFC 2606) so it can never
    /// be mistaken for a reachable endpoint.
    /// </summary>
    public const string AuthorityUrlPlaceholder = "https://authority.invalid";

    private static readonly string[] FlowUrlKeys = ["authorizationUrl", "tokenUrl", "refreshUrl"];

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Returns <paramref name="documentJson"/> with its environment-derived nodes replaced by
    /// deterministic placeholders; everything else is passed through untouched.
    /// </summary>
    /// <param name="documentJson">The OpenAPI document as fetched from the producer.</param>
    /// <returns>The normalized document, re-serialized indented for a readable committed artifact.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="documentJson"/> parses to something other than a JSON object.
    /// </exception>
    public static string ScrubEnvironmentDerived(string documentJson)
    {
        if (JsonNode.Parse(documentJson) is not JsonObject document)
        {
            throw new ArgumentException(
                "The OpenAPI document is not a JSON object.", nameof(documentJson));
        }

        if (document["servers"] is JsonArray)
        {
            document["servers"] = new JsonArray(new JsonObject { ["url"] = ServerUrlPlaceholder });
        }

        if (document["components"]?["securitySchemes"] is JsonObject securitySchemes)
        {
            foreach (var scheme in securitySchemes)
            {
                ScrubFlowUrls(scheme.Value?["flows"] as JsonObject);
            }
        }

        return document.ToJsonString(WriteOptions);
    }

    private static void ScrubFlowUrls(JsonObject? flows)
    {
        if (flows is null)
        {
            return;
        }

        foreach (var flow in flows)
        {
            if (flow.Value is not JsonObject flowNode)
            {
                continue;
            }

            foreach (var urlKey in FlowUrlKeys)
            {
                if (flowNode.ContainsKey(urlKey))
                {
                    flowNode[urlKey] = AuthorityUrlPlaceholder;
                }
            }
        }
    }
}
