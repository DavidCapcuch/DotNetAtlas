using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Ordering.Infrastructure.Common.Authorization;

namespace Ordering.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Forges unsigned-but-shape-correct JWTs for the test host. The test host's
/// JwtBearer pipeline is configured (in <see cref="ApiTestFixture"/>) with
/// signature validation switched off, so these tokens authenticate without
/// needing the real Keycloak signing key. Mirrors
/// <c>test/Weather.FunctionalTests/Common/TestClientInfrastructure/FakeTokenCreator.cs</c>.
/// </summary>
public static class FakeTokenCreator
{
    public static string CreateUserToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.NonAuth => string.Empty,
            ClientType.Buyer => CreateToken(TestUsers.BuyerId, "buyer@dotnetatlas.com", roles: []),
            ClientType.OtherBuyer => CreateToken(TestUsers.OtherBuyerId, "other-buyer@dotnetatlas.com", roles: []),
            ClientType.Admin => CreateToken(TestUsers.AdminId, "admin@dotnetatlas.com", roles: [Roles.Admin]),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType)),
        };
    }

    private static string CreateToken(Guid sub, string userName, string[] roles)
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
