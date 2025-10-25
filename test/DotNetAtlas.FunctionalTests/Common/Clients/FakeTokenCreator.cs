using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DotNetAtlas.Infrastructure.Common.Authorization;

namespace DotNetAtlas.FunctionalTests.Common.Clients;

public static class FakeTokenCreator
{
    public static string CreateUserToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.Dev => CreateToken("dev@dotnetatlas.com", [Roles.Developer]),
            ClientType.Pleb => CreateToken("pleb@dotnetatlas.com", []),
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

        var token = new JwtSecurityToken(
            issuer: "NOT CHECKED IN TESTING",
            audience: "NOT CHECKED IN TESTING",
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: null,
            claims: claims);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
