using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Test.Framework.Auth;

namespace Catalog.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Maps a <see cref="ClientType"/> to the claim set the Catalog scope-based
/// policies expect, then delegates signing to <see cref="FakeTokenBuilder"/>.
/// </summary>
public sealed class FakeTokenCreator
{
    public const string CatalogReadScope = "catalog.read";
    public const string CatalogWriteScope = "catalog.write";

    private readonly FakeTokenSigner _signer;

    public FakeTokenCreator(FakeTokenSigner signer)
    {
        _signer = signer;
    }

    public string CreateToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.NonAuth => string.Empty,
            ClientType.ReadOnly => Build(scope: CatalogReadScope),
            ClientType.WriteAdmin => Build(scope: $"{CatalogReadScope} {CatalogWriteScope}"),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType)),
        };
    }

    private string Build(string scope)
    {
        var subject = Guid.CreateVersion7().ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, $"catalog-tester-{subject}@dotnetatlas.test"),
            new("scope", scope),
        };

        return FakeTokenBuilder.SignToken(_signer, claims);
    }
}
