using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Platform.Test.Framework.Auth;

/// <summary>
/// Owns the test host's RSA signing key + the issuer/audience strings the test
/// JwtBearer pipeline trusts. Shared by the per-BC <c>ApiTestFixture</c> (which
/// installs <see cref="SigningKey"/> as
/// <c>TokenValidationParameters.IssuerSigningKey</c> via
/// <see cref="JwtBearerTestExtensions.ConfigureJwtBearerForTests"/>) and the
/// per-BC <c>FakeTokenCreator</c> (which signs forged tokens with
/// <see cref="SigningCredentials"/> via <see cref="FakeTokenBuilder.SignToken"/>),
/// so the in-process auth pipeline validates forged tokens for real — no
/// <c>RequireSignedTokens=false</c> escape hatch needed. Keeps the production
/// hardening (PR #223's PostConfigure re-pinning the five
/// <see cref="TokenValidationParameters"/> flags to <c>true</c>) observable in
/// every functional suite.
///
/// <para>
/// Audience is per-BC (mirrors production where each service has its own
/// audience like <c>payments-service</c>); the fixture passes it via the
/// constructor.
/// </para>
/// </summary>
public sealed class FakeTokenSigner : IDisposable
{
    /// <summary>
    /// Single shared test issuer for every BC — opaque to production code, so
    /// sharing one value across suites is fine and keeps each BC's audience the
    /// only knob worth tuning per-BC.
    /// </summary>
    public const string TestIssuer = "https://tests.dotnetatlas.local/realms/dotnetatlas";

    private readonly RSA _rsa;

    public FakeTokenSigner(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        Audience = audience;
        _rsa = RSA.Create(2048);
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = "dotnetatlas-test-key-1" };
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256);
    }

    public string Audience { get; }

    public RsaSecurityKey SigningKey { get; }

    public SigningCredentials SigningCredentials { get; }

    public void Dispose() => _rsa.Dispose();
}
