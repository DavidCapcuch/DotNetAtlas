using Microsoft.AspNetCore.Authorization;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// HTTP-side scope-claim enforcement helper (ADR-0010 §Scope enforcement on inbound HTTP).
/// Adds a <c>scope</c>-claim requirement plus authenticated-user gate to an
/// <see cref="AuthorizationPolicyBuilder"/>. Pass each allowed scope by calling
/// <see cref="RequireScope"/> once per scope (Keycloak emits the <c>scope</c> claim as a
/// space-separated string; the JWT bearer handler splits it into individual claims per
/// ADR-0010 §Token validation).
/// </summary>
public static class ScopePolicyExtensions
{
    /// <summary>The OAuth2 / OIDC standard claim name carrying issued scopes.</summary>
    public const string ScopeClaimType = "scope";

    /// <summary>
    /// Requires an authenticated principal whose <c>scope</c> claim contains
    /// <paramref name="scope"/>.
    /// </summary>
    public static AuthorizationPolicyBuilder RequireScope(
        this AuthorizationPolicyBuilder builder,
        string scope)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        builder.RequireAuthenticatedUser();
        builder.RequireClaim(ScopeClaimType, scope);
        return builder;
    }
}
