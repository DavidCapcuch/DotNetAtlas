using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Platform.Test.Framework.Auth;

/// <summary>
/// Shared helper that builds + signs a JWT for the in-process test host using
/// a <see cref="FakeTokenSigner"/>. Per-BC <c>FakeTokenCreator</c> types call
/// into this so they only need to own the BC-specific claim-shape logic
/// (which role / scope claims belong to which <c>ClientType</c>).
/// </summary>
public static class FakeTokenBuilder
{
    /// <summary>
    /// Builds a signed JWT whose <c>iss</c> + <c>aud</c> match the
    /// <see cref="FakeTokenSigner"/>'s configured values, signed with the
    /// signer's RSA key. Lifetime defaults to one hour from
    /// <see cref="DateTime.UtcNow"/>.
    /// </summary>
    public static string SignToken(
        FakeTokenSigner signer,
        IEnumerable<Claim> claims,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(claims);

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = FakeTokenSigner.TestIssuer,
            Audience = signer.Audience,
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1)),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = signer.SigningCredentials,
        };

        return handler.CreateToken(descriptor);
    }
}
