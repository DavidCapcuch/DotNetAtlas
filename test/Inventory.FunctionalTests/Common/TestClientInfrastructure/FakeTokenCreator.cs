using System.Security.Claims;
using Inventory.Api.Common.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Test.Framework.Auth;

namespace Inventory.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Maps a <see cref="ClientType"/> to the claim set the Inventory authorization
/// policies expect — the space-separated <c>scope</c> claim that
/// <see cref="AuthPolicies"/> reads, plus the flat realm-role
/// claim (<see cref="ClaimTypes.Role"/>) that <c>WritePolicy</c>'s
/// <c>RequireRole</c> reads — then delegates signing to <see cref="FakeTokenBuilder"/>.
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
            // Service-to-service read (e.g. BFF): inventory.read scope, no role.
            ClientType.ReadOnly => CreateToken(Scopes.InventoryRead, roles: []),
            // Admin happy path: admin role AND inventory.write scope — satisfies WritePolicy.
            ClientType.Commands => CreateToken(Scopes.InventoryWrite, roles: [Roles.Admin]),
            // Defense-in-depth negative: inventory.write scope but NO admin role — must be
            // rejected by WritePolicy's RequireRole half (proves the role gate, not just the scope).
            ClientType.WriteScopeNoAdmin => CreateToken(Scopes.InventoryWrite, roles: []),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType),
                $"{clientType} has no associated token; callers should skip CreateToken for NonAuth."),
        };
    }

    private string CreateToken(string scope, string[] roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
            // RFC 6749 § 3.3 — space-separated. Production tokens often include
            // openid + profile alongside the API scopes; mirror that here so
            // the policy-side scope parser is exercised against realistic input.
            new("scope", $"openid profile {scope}"),
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return FakeTokenBuilder.SignToken(_signer, claims);
    }
}
