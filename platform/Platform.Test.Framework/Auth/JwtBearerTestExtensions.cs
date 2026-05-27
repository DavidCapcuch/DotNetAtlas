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
    /// matching <c>iss</c>. <b>ValidAudience is intentionally NOT overridden</b> —
    /// the BC's own <c>Authentication:JwtBearer:TokenValidationParameters:ValidAudience</c>
    /// in appsettings is the single source of truth (since the 2026-05 normalisation audit,
    /// <c>JwtBearerConfigurator</c> no longer defaults <c>ValidAudience</c> from
    /// <c>ServiceAuthOptions.ServiceName</c>; a BC that omits the appsettings pin fails
    /// closed). The assertion in PostConfigure makes any drift between
    /// <see cref="FakeTokenSigner.Audience"/> and the BC's effective <c>ValidAudience</c>
    /// loud at first auth resolution, so a misconfigured production audience can no longer
    /// pass FunctionalTests silently.
    ///
    /// <para>
    /// Splits the override across two option-pipeline phases:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Configure</b> — clear <c>Authority</c> +
    /// <c>MetadataAddress</c> so the framework's
    /// <c>JwtBearerPostConfigureOptions</c> doesn't build an
    /// <c>OpenIdConnect ConfigurationManager</c> that would try to fetch JWKS
    /// from a non-existent Keycloak. Also disables
    /// <c>RequireHttpsMetadata</c> so the framework's HTTPS-authority guard
    /// doesn't fire on the cleared authority.</description></item>
    /// <item><description><b>PostConfigure</b> — install the test signing key
    /// + <c>ValidIssuer</c>; assert <c>ValidAudience</c> already matches
    /// <see cref="FakeTokenSigner.Audience"/>. Must run after the BC's
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

                // ValidAudience is INTENTIONALLY not assigned here. The BC's own
                // configuration pipeline owns it (production source of truth). If it
                // drifts from FakeTokenSigner.Audience, surface that loudly instead of
                // silently overriding — this is the exact bug class normalised in
                // 2026-05 (FunctionalTests masked a misconfigured production audience).
                var effective = options.TokenValidationParameters.ValidAudience;
                if (!string.Equals(effective, signer.Audience, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Test setup error: BC's effective JwtBearer ValidAudience " +
                        $"('{effective ?? "<null>"}') does not match the FakeTokenSigner " +
                        $"audience ('{signer.Audience}'). Either the BC's auth config is " +
                        $"broken (check appsettings 'Authentication:JwtBearer:TokenValidationParameters:ValidAudience') " +
                        $"or the fixture constructed FakeTokenSigner with a stale audience. " +
                        $"The test framework no longer papers over this.");
                }
            });

        return services;
    }
}
