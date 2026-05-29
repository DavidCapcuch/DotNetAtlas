using Microsoft.AspNetCore.Authorization;

namespace Inventory.Api.Common.Authorization;

/// <summary>
/// Scope-based authorization policies for the Inventory admin HTTP surface
/// (ADR-0010). Read-only endpoints require <c>inventory.read</c>; mutating
/// endpoints (Receive, Adjust) require <c>inventory.write</c>.
/// </summary>
/// <remarks>
/// <para>
/// Keycloak emits the <c>scope</c> claim as a single space-separated string per RFC 6749
/// (e.g. <c>"openid profile inventory.read inventory.write"</c>). The
/// policies below split that string and check membership; this avoids depending on
/// Microsoft.Identity.Web for what is otherwise a one-line predicate.
/// </para>
/// <para>
/// <b>Scope hierarchy:</b> <c>inventory.write</c> implies <c>inventory.read</c>.
/// Admin tokens carrying only the write scope still satisfy the read policy.
/// </para>
/// <para>
/// <b>Why no separate "adjust" scope:</b> the realm intentionally re-uses
/// <c>inventory.write</c> across both Receive and Adjust admin endpoints to keep
/// the realm small. Split into a dedicated admin scope if operations needs the audit
/// separation; the policy class is the seam to update.
/// </para>
/// </remarks>
internal static class InventoryAuthorizationPolicies
{
    public const string ReadPolicy = "InventoryReadScope";
    public const string WritePolicy = "InventoryWriteScope";

    public const string ReadScope = "inventory.read";
    public const string WriteScope = "inventory.write";

    private const string ScopeClaimType = "scope";

    public static AuthorizationOptions AddInventoryScopePolicies(this AuthorizationOptions options)
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
        var scopeClaims = ctx.User.FindAll(ScopeClaimType);
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
