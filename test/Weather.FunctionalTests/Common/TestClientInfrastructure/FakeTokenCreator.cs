using System.Security.Claims;
using Platform.Test.Framework.Auth;
using Weather.Infrastructure.Common.Authorization;

namespace Weather.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Maps a <see cref="ClientType"/> to the claim set Weather's authorization
/// policies expect, then delegates signing to <see cref="FakeTokenBuilder"/>.
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
            ClientType.Dev => FakeTokenBuilder.SignToken(_signer,
                BuildClaims("dev@dotnetatlas.com", [Roles.Developer])),
            ClientType.RegularUser => FakeTokenBuilder.SignToken(_signer,
                BuildClaims("pleb@dotnetatlas.com", [])),
            ClientType.NonAuth => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(clientType))
        };
    }

    private static List<Claim> BuildClaims(string userName, string[] roles)
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

        return claims;
    }
}
