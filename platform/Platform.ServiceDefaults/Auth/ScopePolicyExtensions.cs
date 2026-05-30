using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// HTTP-side scope-claim enforcement helper (ADR-0010 §Scope enforcement on inbound HTTP).
/// Keycloak emits granted scopes as a single space-separated <c>scope</c> claim (RFC 6749);
/// some IdPs instead emit one <c>scope</c> claim per scope. <see cref="RequireAnyScope"/>
/// handles both shapes and gates the policy on an authenticated principal holding at least
/// one of the supplied scopes — the read-or-write hierarchy every BC needs (a token bearing
/// the write scope also satisfies the read policy).
/// </summary>
public static class ScopePolicyExtensions
{
    /// <summary>The OAuth2 / OIDC standard claim name carrying issued scopes.</summary>
    public const string ScopeClaimType = "scope";

    /// <summary>
    /// Requires an authenticated principal whose <c>scope</c> claim(s) contain at least one of
    /// <paramref name="scopes"/>. Each claim value is split on spaces before matching, so a
    /// single <c>"openid profile catalog.read"</c> claim and three separate <c>scope</c> claims
    /// behave identically.
    /// </summary>
    public static AuthorizationPolicyBuilder RequireAnyScope(
        this AuthorizationPolicyBuilder builder,
        params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Length == 0)
        {
            throw new ArgumentException("At least one scope must be supplied.", nameof(scopes));
        }

        foreach (var scope in scopes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        }

        builder.RequireAuthenticatedUser();
        builder.RequireAssertion(ctx => HasAnyScope(ctx.User, scopes));
        return builder;
    }

    private static bool HasAnyScope(ClaimsPrincipal user, string[] required)
    {
        foreach (var claim in user.FindAll(ScopeClaimType))
        {
            foreach (var granted in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var needle in required)
                {
                    if (string.Equals(granted, needle, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
