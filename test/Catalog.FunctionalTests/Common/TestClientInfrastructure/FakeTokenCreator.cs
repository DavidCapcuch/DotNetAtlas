using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Catalog.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Issues unsigned JWTs for the in-process test host. The fixture's
/// <c>JwtBearerOptions.SignatureValidator</c> override accepts unsigned tokens; we still emit
/// a real JWT so the <c>scope</c> claim is parsed by ASP.NET's claims pipeline exactly as it
/// would be in production.
/// </summary>
public static class FakeTokenCreator
{
    public const string CatalogReadScope = "catalog.read";
    public const string CatalogWriteScope = "catalog.write";

    public static string CreateToken(ClientType clientType)
    {
        return clientType switch
        {
            ClientType.NonAuth => string.Empty,
            ClientType.ReadOnly => Build(scope: CatalogReadScope),
            ClientType.WriteAdmin => Build(scope: $"{CatalogReadScope} {CatalogWriteScope}"),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType)),
        };
    }

    private static string Build(string scope)
    {
        var subject = Guid.CreateVersion7().ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, $"catalog-tester-{subject}@dotnetatlas.test"),
            new("scope", scope),
        };

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "NOT CHECKED IN TESTING",
            Audience = "NOT CHECKED IN TESTING",
            Expires = DateTime.UtcNow.AddHours(1),
            Subject = new ClaimsIdentity(claims),
        };

        return handler.CreateToken(descriptor);
    }
}
