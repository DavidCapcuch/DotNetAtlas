using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Test.Framework.Auth;

namespace Basket.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Builds claims for Basket's regular-user persona (Basket only carries a
/// single authenticated archetype) and delegates signing to
/// <see cref="FakeTokenBuilder"/>. Includes an explicit JWT <c>sub</c> claim
/// so <c>Basket.Api.Common.Extensions.ClaimsPrincipalExtensions.GetUserIdFromSubClaim</c>
/// can read the user id without relying on .NET's
/// <see cref="ClaimTypes.NameIdentifier"/> aliasing.
/// </summary>
public sealed class FakeTokenCreator
{
    private readonly FakeTokenSigner _signer;

    public FakeTokenCreator(FakeTokenSigner signer)
    {
        _signer = signer;
    }

    public string CreateUserToken(ClientType clientType, Guid? userId = null)
    {
        return clientType switch
        {
            ClientType.RegularUser => Build(userId ?? Guid.CreateVersion7()),
            ClientType.NonAuth => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(clientType)),
        };
    }

    private string Build(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, $"user-{userId}@dotnetatlas.test"),
        };

        return FakeTokenBuilder.SignToken(_signer, claims);
    }
}
