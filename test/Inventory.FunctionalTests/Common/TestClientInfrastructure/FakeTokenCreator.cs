using System.Security.Claims;
using Inventory.API.Common.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Creates unsigned JWTs for the in-process test host. Signature validation is
/// disabled in <see cref="InventoryApiFixture"/> so the JWT just needs the
/// expected claims — the relevant claim for Inventory's policy gates is the
/// space-separated <c>scope</c> claim parsed by
/// <see cref="InventoryAuthorizationPolicies.HasAnyScope"/>.
/// </summary>
internal static class FakeTokenCreator
{
    public static string CreateToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.ReadOnly => CreateScopedToken(InventoryAuthorizationPolicies.ReadScope),
            ClientType.Commands => CreateScopedToken(InventoryAuthorizationPolicies.CommandsScope),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType),
                $"{clientType} has no associated token; callers should skip CreateToken for NonAuth."),
        };
    }

    private static string CreateScopedToken(string scope)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
            // RFC 6749 § 3.3 — space-separated. Production tokens often include
            // openid + profile alongside the API scopes; mirror that here so
            // the policy-side scope parser is exercised against realistic input.
            new("scope", $"openid profile {scope}"),
        };

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "NOT CHECKED IN TESTING",
            Audience = "NOT CHECKED IN TESTING",
            Expires = DateTime.UtcNow.AddHours(1),
            Subject = new ClaimsIdentity(claims, "TestAuth"),
        };

        return handler.CreateToken(descriptor);
    }
}
