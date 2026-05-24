using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Test.Framework.Auth;

/// <summary>
/// One-call test-host JwtBearer relaxation that keeps every
/// <c>TokenValidationParameters</c> flag at its production default of
/// <c>true</c> by registering a <see cref="FakeTokenSigner"/>'s RSA key as the
/// trusted signing key. Replaces the pre-#223 pattern of disabling
/// signature / issuer / audience / lifetime validation in tests, which left
/// the production hardening (the BC's own <c>PostConfigure</c> re-pinning
/// those flags) untestable.
/// </summary>
public static class JwtBearerTestExtensions
{
    /// <summary>
    /// Wires the in-process test host's JwtBearer scheme to trust the
    /// <paramref name="signer"/>'s RSA key (and only that key), with the
    /// matching <c>iss</c> / <c>aud</c>. Splits the override across two
    /// option-pipeline phases:
    ///
    /// <list type="bullet">
    /// <item><description><b>Configure</b> — clear <c>Authority</c> +
    /// <c>MetadataAddress</c> so the framework's
    /// <c>JwtBearerPostConfigureOptions</c> doesn't build an
    /// <c>OpenIdConnect ConfigurationManager</c> that would try to fetch JWKS
    /// from a non-existent Keycloak. Also disables
    /// <c>RequireHttpsMetadata</c> so the framework's HTTPS-authority guard
    /// doesn't fire on the cleared authority.</description></item>
    /// <item><description><b>PostConfigure</b> — install the test signing key
    /// + <c>ValidIssuer</c> + <c>ValidAudience</c>. Must run after the BC's
    /// own #223-style <c>PostConfigure</c> that re-pins the five validation
    /// flags to <c>true</c> — and it does, because <c>ConfigureTestServices</c>
    /// registers later than <c>Program.cs</c> and <c>PostConfigure</c>
    /// callbacks run in registration order.</description></item>
    /// </list>
    ///
    /// Call this from a fixture's <c>ConfigureTestServices</c> callback.
    /// </summary>
    public static IServiceCollection ConfigureJwtBearerForTests(
        this IServiceCollection services,
        FakeTokenSigner signer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(signer);

        services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                options.Authority = string.Empty;
                options.MetadataAddress = string.Empty;
                options.RequireHttpsMetadata = false;
            });

        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                options.TokenValidationParameters.IssuerSigningKey = signer.SigningKey;
                options.TokenValidationParameters.ValidIssuer = FakeTokenSigner.TestIssuer;
                options.TokenValidationParameters.ValidAudience = signer.Audience;
            });

        return services;
    }
}
