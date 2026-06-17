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

    /// <summary>
    /// Builds a signed JWT carrying an <b>explicit multi-valued</b> <c>aud</c> claim, mirroring the
    /// production <c>dotnetatlas-swagger</c> token (whose audience mappers stamp several service
    /// audiences onto a single token). Signed with the <paramref name="signer"/>'s key so the
    /// in-process validator trusts it, but the audiences are supplied explicitly — independent of
    /// <see cref="FakeTokenSigner.Audience"/> — so a BC can assert its resource server accepts a
    /// multi-aud token that <i>contains</i> its audience and rejects one that <i>omits</i> it
    /// (<c>ValidateAudience</c> is any-match). <paramref name="audiences"/> must be non-empty.
    /// </summary>
    public static string SignTokenWithAudiences(
        FakeTokenSigner signer,
        IEnumerable<Claim> claims,
        IReadOnlyCollection<string> audiences,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(audiences);
        if (audiences.Count == 0)
        {
            throw new ArgumentException("At least one audience is required.", nameof(audiences));
        }

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = FakeTokenSigner.TestIssuer,
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1)),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = signer.SigningCredentials,
        };

        // Populate the multi-valued `aud`; leave the singular Audience unset so the array is the
        // sole audience source (SecurityTokenDescriptor.Audiences emits a JSON array when populated).
        foreach (var audience in audiences)
        {
            descriptor.Audiences.Add(audience);
        }

        return handler.CreateToken(descriptor);
    }
}
