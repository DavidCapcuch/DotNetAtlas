using Microsoft.AspNetCore.Authorization;

namespace Catalog.API.Common.Authorization;

/// <summary>
/// Scope-based authorization policies for the Catalog HTTP surface (ADR-0010).
/// Queries require <c>catalog.read</c>; admin commands require <c>catalog.write</c>.
/// </summary>
/// <remarks>
/// <para>
/// Keycloak emits the <c>scope</c> claim as a single space-separated string per RFC 6749
/// (e.g. <c>"openid profile catalog.read catalog.write"</c>). The policies below split that
/// string and check membership; this avoids depending on Microsoft.Identity.Web for what is
/// otherwise a one-line predicate.
/// </para>
/// <para>
/// <b>Scope hierarchy:</b> <c>catalog.write</c> implies <c>catalog.read</c>. Tokens carrying
/// only <c>catalog.write</c> still satisfy the read policy — admins can call query endpoints
/// without a separate scope grant.
/// </para>
/// </remarks>
internal static class CatalogAuthorizationPolicies
{
    public const string ReadPolicy = "CatalogReadScope";
    public const string WritePolicy = "CatalogWriteScope";

    public const string ReadScope = "catalog.read";
    public const string WriteScope = "catalog.write";

    // CAT-SEC-016 / #212 (Wave-1 closeout): Keycloak emits the claim as "scope" (RFC 6749);
    // Auth0 and some other IdPs emit it as "scp". Accept either claim name so the policies
    // work uniformly when the upstream IdP swaps.
    private static readonly string[] ScopeClaimTypes = ["scope", "scp"];

    public static AuthorizationOptions AddCatalogScopePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(ReadPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasAnyScope(ctx, ReadScope, WriteScope)));

        options.AddPolicy(WritePolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasAnyScope(ctx, WriteScope)));

        return options;
    }

    private static bool HasAnyScope(AuthorizationHandlerContext ctx, params string[] required)
    {
        var scopeClaims = ScopeClaimTypes.SelectMany(ctx.User.FindAll);
        foreach (var claim in scopeClaims)
        {
            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var needle in required)
                {
                    if (string.Equals(scope, needle, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
