using System.Security.Claims;
using Payments.Infrastructure.Common.Authorization;
using Platform.Test.Framework.Auth;

namespace Payments.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Maps a <see cref="ClientType"/> to the claim set the Payments authorization
/// policies expect, then delegates signing to <see cref="FakeTokenBuilder"/>
/// using the fixture-owned <see cref="FakeTokenSigner"/>. The resulting tokens
/// are signed with the test RSA key and validated for real by the in-process
/// JwtBearer pipeline — every <c>TokenValidationParameters</c> flag stays at
/// its production default of <c>true</c>.
/// </summary>
public sealed class FakeTokenCreator
{
    private readonly FakeTokenSigner _signer;

    public FakeTokenCreator(FakeTokenSigner signer)
    {
        _signer = signer;
    }

    public string CreateUserToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.NonAuth => string.Empty,
            ClientType.User => FakeTokenBuilder.SignToken(_signer, BuildClaims(
                TestUsers.UserId, "user@dotnetatlas.com", roles: [], scopes: [])),
            ClientType.AdminWithoutScope => FakeTokenBuilder.SignToken(_signer, BuildClaims(
                TestUsers.AdminId, "admin@dotnetatlas.com", roles: [Roles.Admin], scopes: [])),
            ClientType.Admin => FakeTokenBuilder.SignToken(_signer, BuildClaims(
                TestUsers.AdminId, "admin@dotnetatlas.com", roles: [Roles.Admin], scopes: [Scopes.PaymentsRead])),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType)),
        };
    }

    private static List<Claim> BuildClaims(Guid sub, string userName, string[] roles, string[] scopes)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            // ClaimTypes.NameIdentifier is the framework-mapped form of the
            // OAuth/OIDC `sub` claim under the default JwtBearer mapping.
            new(ClaimTypes.NameIdentifier, sub.ToString()),
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (scopes.Length > 0)
        {
            // Keycloak emits scopes as a single space-separated `scope` claim.
            // The PaymentsAdmin policy splits and matches.
            claims.Add(new Claim("scope", string.Join(' ', scopes)));
        }

        return claims;
    }
}
