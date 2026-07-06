using System.Security.Claims;

namespace EShop.BFF.Api.Common;

/// <summary>Buyer-identity helpers shared by the required-auth BFF endpoints (bff.md § 2.3).</summary>
internal static class BffUser
{
    /// <summary>
    /// Resolves the buyer id from the authenticated principal. Prefers the raw <c>sub</c>; falls back to
    /// <see cref="ClaimTypes.NameIdentifier"/> (JwtBearer maps <c>sub</c> onto it when <c>MapInboundClaims</c>
    /// is on). Mirrors Basket's <c>GetUserIdFromSubClaim</c>. Returns <c>false</c> for an unparseable / absent
    /// <c>sub</c> so the caller can fail closed.
    /// </summary>
    public static bool TryGetBuyerId(ClaimsPrincipal user, out Guid userId)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}
