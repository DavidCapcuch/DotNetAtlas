using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Payments.Infrastructure.Common.Authorization;

namespace Payments.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Forges unsigned-but-shape-correct JWTs for the test host. The test host's
/// JwtBearer pipeline is configured (in <see cref="ApiTestFixture"/>) with
/// signature validation switched off, so these tokens authenticate without
/// needing the real Keycloak signing key. Mirrors the Ordering precedent
/// (<c>test/Ordering.FunctionalTests/Common/TestClientInfrastructure/FakeTokenCreator.cs</c>).
/// </summary>
public static class FakeTokenCreator
{
    public static string CreateUserToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.NonAuth => string.Empty,
            ClientType.User => CreateToken(
                TestUsers.UserId,
                "user@dotnetatlas.com",
                roles: [],
                scopes: []),
            ClientType.AdminWithoutScope => CreateToken(
                TestUsers.AdminId,
                "admin@dotnetatlas.com",
                roles: [Roles.Admin],
                scopes: []),
            ClientType.Admin => CreateToken(
                TestUsers.AdminId,
                "admin@dotnetatlas.com",
                roles: [Roles.Admin],
                scopes: [Scopes.PaymentsRead]),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType)),
        };
    }

    private static string CreateToken(Guid sub, string userName, string[] roles, string[] scopes)
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

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "NOT CHECKED IN TESTING",
            Audience = "NOT CHECKED IN TESTING",
            Expires = DateTime.UtcNow.AddHours(1),
            Subject = new ClaimsIdentity(claims),
        };

        return handler.CreateToken(descriptor);
    }
}
