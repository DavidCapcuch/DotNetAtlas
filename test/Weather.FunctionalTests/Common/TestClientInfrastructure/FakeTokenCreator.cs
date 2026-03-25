using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Weather.Infrastructure.Common.Authorization;

namespace Weather.FunctionalTests.Common.TestClientInfrastructure;

public static class FakeTokenCreator
{
    public static string CreateUserToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.Dev => CreateToken("dev@dotnetatlas.com", [Roles.Developer]),
            ClientType.RegularUser => CreateToken("pleb@dotnetatlas.com", []),
            ClientType.NonAuth => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(clientType))
        };
    }

    private static string CreateToken(string userName, string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())
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
            Subject = new ClaimsIdentity(claims)
        };

        return handler.CreateToken(descriptor);
    }
}
