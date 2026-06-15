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
            ClientType.RegularUser => FakeTokenBuilder.SignToken(_signer, BuildClaims(userId ?? Guid.CreateVersion7())),
            ClientType.NonAuth => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(clientType))
        };
    }

    /// <summary>
    /// Mints a recipient token whose <c>aud</c> is the explicit multi-valued <paramref name="audiences"/>
    /// array — the production <c>dotnetatlas-swagger</c> token shape (one login, every browser-facing
    /// service audience). Lets the bell-hub tests assert acceptance of a multi-aud token and rejection
    /// of one whose audiences omit <c>notifications-service</c>.
    /// </summary>
    public string CreateUserTokenWithAudiences(Guid userId, IReadOnlyCollection<string> audiences)
    {
        return FakeTokenBuilder.SignTokenWithAudiences(_signer, BuildClaims(userId), audiences);
    }

    /// <summary>
    /// Mints a token carrying the given <paramref name="audiences"/> but **no** <c>sub</c> /
    /// <c>NameIdentifier</c> claim — the shape a Keycloak client lacking a subject mapper issues into
    /// its access token. Pins that the bell rejects a token it can authenticate but cannot key to a
    /// recipient (the `dotnetatlas-swagger` realm fix adds a `subject` mapper precisely to avoid this).
    /// </summary>
    public string CreateSubjectlessTokenWithAudiences(IReadOnlyCollection<string> audiences)
    {
        return FakeTokenBuilder.SignTokenWithAudiences(_signer, claims: [], audiences);
    }

    private static List<Claim> BuildClaims(Guid userId)
    {
        return
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, $"user-{userId}@dotnetatlas.test")
        ];
    }
}
