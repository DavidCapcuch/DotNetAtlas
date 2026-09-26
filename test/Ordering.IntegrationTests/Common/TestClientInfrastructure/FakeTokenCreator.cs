using System.Security.Claims;
using Ordering.Api.Common.Authorization;
using Platform.Test.Framework.Auth;

namespace Ordering.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Maps a <see cref="ClientType"/> to the claim set the Ordering authorization
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
            ClientType.NonAuth => string.Empty,
            ClientType.Buyer => FakeTokenBuilder.SignToken(_signer,
                BuildClaims(TestUsers.BuyerId, "buyer@dotnetatlas.com", roles: [])),
            ClientType.OtherBuyer => FakeTokenBuilder.SignToken(_signer,
                BuildClaims(TestUsers.OtherBuyerId, "other-buyer@dotnetatlas.com", roles: [])),
            ClientType.Admin => FakeTokenBuilder.SignToken(_signer,
                BuildClaims(TestUsers.AdminId, "admin@dotnetatlas.com", roles: [Roles.Admin])),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType)),
        };
    }

    private static List<Claim> BuildClaims(Guid sub, string userName, string[] roles)
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

        return claims;
    }
}
