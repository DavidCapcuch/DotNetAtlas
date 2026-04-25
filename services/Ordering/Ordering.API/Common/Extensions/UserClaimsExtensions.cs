using System.Security.Claims;
using Ordering.Infrastructure.Common.Authorization;

namespace Ordering.API.Common.Extensions;

/// <summary>
/// Helpers that read the Ordering-specific bits of the calling user's
/// <see cref="ClaimsPrincipal"/>. Keep all "what does this token mean for
/// Ordering" logic in one place so endpoint code stays declarative.
/// </summary>
internal static class UserClaimsExtensions
{
    /// <summary>
    /// Pulls the buyer id from the JWT subject claim. The HTTP pipeline
    /// guarantees a valid token has reached the endpoint; a missing or
    /// non-Guid <c>sub</c> here is bug-class (token shape doesn't match the
    /// realm config) and surfaces as <see langword="null"/> so the endpoint
    /// can short-circuit with 401.
    /// </summary>
    public static Guid? GetBuyerIdOrNull(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        // ClaimTypes.NameIdentifier is the framework-mapped form of the
        // OAuth/OIDC `sub` claim under the default JwtBearer mapping.
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// True when the caller's token carries the Keycloak realm role that
    /// backs <see cref="AuthPolicies.OrderingAdmin"/>.
    /// </summary>
    public static bool IsOrderingAdmin(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsInRole(Roles.Admin);
    }
}
