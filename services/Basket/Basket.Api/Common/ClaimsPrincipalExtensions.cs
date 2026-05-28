using System.Security.Claims;
using Platform.SharedKernel.Exceptions;

namespace Basket.Api.Common;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the OAuth2 <c>sub</c> claim and parses it as a <see cref="Guid"/>. The Basket
    /// service-design treats the Keycloak <c>sub</c> as the canonical user-id (see
    /// <c>docs/bc-design/use-cases.md § 2.1</c>). The auth pipeline guarantees that the
    /// claim is present once the endpoint is reached; absence indicates a misconfigured
    /// pipeline and is therefore a <see cref="DataIntegrityException"/>.
    /// </summary>
    public static Guid GetUserIdFromSubClaim(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(JwtClaimTypes.Subject)
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DataIntegrityException(
                errorCode: "Basket.Auth.SubClaimMissing",
                message: "Authenticated principal has no 'sub' claim — JWT validation pipeline misconfigured.");
        }

        if (!Guid.TryParse(raw, out var userId))
        {
            throw new DataIntegrityException(
                errorCode: "Basket.Auth.SubClaimNotGuid",
                message: $"Authenticated principal 'sub' claim '{raw}' is not a valid GUID.");
        }

        return userId;
    }

    private static class JwtClaimTypes
    {
        public const string Subject = "sub";
    }
}
