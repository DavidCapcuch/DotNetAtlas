using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Test.Framework.Auth;

namespace Notifications.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Mints a bell-hub access token for a given recipient and delegates signing to
/// <see cref="FakeTokenBuilder"/>. Includes an explicit JWT <c>sub</c> claim (= NameIdentifier) so
/// <c>SubClaimUserIdProvider</c> resolves <c>Context.UserIdentifier</c> to the recipient's id — the
/// hub keys its per-user group by <c>sub</c>. Mirrors the Basket BC's FakeTokenCreator.
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
            _ => throw new ArgumentOutOfRangeException(nameof(clientType))
        };
    }

    private string Build(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, $"user-{userId}@dotnetatlas.test")
        };

        return FakeTokenBuilder.SignToken(_signer, claims);
    }
}
