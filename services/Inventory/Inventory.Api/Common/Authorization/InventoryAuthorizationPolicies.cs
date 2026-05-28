using Microsoft.AspNetCore.Authorization;

namespace Inventory.Api.Common.Authorization;

/// <summary>
/// Scope-based authorization policies for the Inventory admin HTTP surface
/// (ADR-0010). Read-only endpoints require <c>inventory.read</c>; mutating
/// endpoints (Receive, Adjust) require <c>inventory.commands.reserve</c>.
/// </summary>
/// <remarks>
/// <para>
/// Keycloak emits the <c>scope</c> claim as a single space-separated string per RFC 6749
/// (e.g. <c>"openid profile inventory.read inventory.commands.reserve"</c>). The
/// policies below split that string and check membership; this avoids depending on
/// Microsoft.Identity.Web for what is otherwise a one-line predicate.
/// </para>
/// <para>
/// <b>Scope hierarchy:</b> <c>inventory.commands.reserve</c> implies <c>inventory.read</c>.
/// Admin tokens carrying only the commands scope still satisfy the read policy.
/// </para>
/// <para>
/// <b>Why no separate "adjust" scope:</b> the realm intentionally re-uses
/// <c>inventory.commands.reserve</c> for both saga-driven reservations AND admin Receive /
/// Adjust to keep the realm small. Split into a dedicated admin scope if operations needs
/// the audit separation; the policy class is the seam to update.
/// </para>
/// <para>
/// <b>Why no Confirm / Release policies:</b> the realm declares
/// <c>inventory.commands.{confirm,release}</c> for the saga's Kafka-driven path, not for
/// HTTP. There is no admin HTTP endpoint for confirm or release; those scopes are
/// consumed only by the broker on the <c>inventory.reservation-commands</c> topic.
/// </para>
/// </remarks>
internal static class InventoryAuthorizationPolicies
{
    public const string ReadPolicy = "InventoryReadScope";
    public const string CommandsPolicy = "InventoryCommandsScope";

    public const string ReadScope = "inventory.read";
    public const string CommandsScope = "inventory.commands.reserve";

    private const string ScopeClaimType = "scope";

    public static AuthorizationOptions AddInventoryScopePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(ReadPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasAnyScope(ctx, ReadScope, CommandsScope)));

        options.AddPolicy(CommandsPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasAnyScope(ctx, CommandsScope)));

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
