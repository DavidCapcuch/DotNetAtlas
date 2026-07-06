using System.Text.Json;
using Platform.Test.Framework;

namespace EShop.BFF.IntegrationTests.Auth;

/// <summary>
/// Guards the dev human-admin <c>dotnetatlas-swagger</c> client's unconditional audience surface in the
/// committed <c>realm-export.json</c>: it must stamp a BC <c>aud</c> on every token only for the BCs an admin
/// reaches with <b>no scope</b> to carry it — <c>ordering-service</c>/<c>invoicing-service</c> (role-only admin
/// endpoints), <c>notifications-service</c> (bell hub), and <c>bff</c> (token-exchange holder). Re-adding a
/// <c>basket</c>/<c>catalog</c>/<c>inventory</c>/<c>payments</c> audience would reopen the direct-path bypass
/// BFF mediation closes (the per-audience reasoning lives in the assertion messages below; full rationale +
/// the kept-vs-dropped split: <c>src/keycloak/service-scope-matrix.md</c> and ADR-0010 §"Role vs scope
/// canonical model" / §2026-06-06).
/// </summary>
public sealed class SwaggerClientAudienceMapperTests
{
    private const string SwaggerClientId = "dotnetatlas-swagger";

    private static readonly string[] ExpectedUnconditionalAudiences =
        ["bff", "ordering-service", "invoicing-service", "notifications-service"];

    private static readonly string[] ForbiddenAudiences =
        ["basket-service", "catalog-service", "inventory-service", "payments-service"];

    [Fact]
    [Trait("Category", "security")]
    public void SwaggerClient_StampsUnconditionalAudiences_OnlyForTheNoScopePathBoundedContexts()
    {
        var audiences = ReadSwaggerUnconditionalAudiences();

        using (new AssertionScope())
        {
            audiences.Should().BeEquivalentTo(
                ExpectedUnconditionalAudiences,
                "the swagger dev tool stamps an unconditional BC aud only where the admin reaches the BC with no scope "
                + "(ordering/invoicing role-only, notifications authorize-only) plus bff for the token-exchange holder constraint");

            foreach (var forbidden in ForbiddenAudiences)
            {
                audiences.Should().NotContain(
                    forbidden,
                    "{0} is either fully BFF-mediated (basket) or role+scope (catalog/inventory/payments, audience rides the "
                    + "requested optional scope) — an unconditional mapper would reopen the direct-path bypass",
                    forbidden);
            }
        }
    }

    private static List<string> ReadSwaggerUnconditionalAudiences()
    {
        var realmPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(), "src", "keycloak", "realm-export.json");

        using var realm = JsonDocument.Parse(File.ReadAllText(realmPath));

        var swagger = realm.RootElement
            .GetProperty("clients")
            .EnumerateArray()
            .Single(c => c.GetProperty("clientId").GetString() == SwaggerClientId);

        return swagger
            .GetProperty("protocolMappers")
            .EnumerateArray()
            .Where(m => m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper")
            .Select(m => m.GetProperty("config").GetProperty("included.client.audience").GetString()!)
            .ToList();
    }
}
