using System.Security.Claims;
using Inventory.Api.Common.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Test.Framework.Auth;

namespace Inventory.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Maps a <see cref="ClientType"/> to the space-separated <c>scope</c> claim
/// that <see cref="InventoryAuthorizationPolicies.HasAnyScope"/> reads, then
/// delegates signing to <see cref="FakeTokenBuilder"/>.
/// </summary>
public sealed class FakeTokenCreator
{
    private readonly FakeTokenSigner _signer;

    public FakeTokenCreator(FakeTokenSigner signer)
    {
        _signer = signer;
    }

    public string CreateToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.ReadOnly => CreateScopedToken(InventoryAuthorizationPolicies.ReadScope),
            ClientType.Commands => CreateScopedToken(InventoryAuthorizationPolicies.CommandsScope),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType),
                $"{clientType} has no associated token; callers should skip CreateToken for NonAuth."),
        };
    }

    private string CreateScopedToken(string scope)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
            // RFC 6749 § 3.3 — space-separated. Production tokens often include
            // openid + profile alongside the API scopes; mirror that here so
            // the policy-side scope parser is exercised against realistic input.
            new("scope", $"openid profile {scope}"),
        };

        return FakeTokenBuilder.SignToken(_signer, claims);
    }
}
