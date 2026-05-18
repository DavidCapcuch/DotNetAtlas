using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Auth;

namespace Basket.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/> and
    /// allows callers to override defaults through <c>Authentication:JwtBearer</c> configuration.
    /// Basket is API-only — no Cookie / OIDC schemes (those are Web-UI concerns owned by the BFF).
    /// </summary>
    /// <remarks>
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments we add a
    /// post-configure guard that asserts <c>RequireSignedTokens</c>,
    /// <c>ValidateIssuerSigningKey</c>, and <c>RequireHttpsMetadata</c> remain enabled. The
    /// <c>configuration.Bind</c> call below otherwise lets a misconfigured env-var silently
    /// relax these flags, and <c>appsettings.json</c> ships <c>RequireHttpsMetadata: false</c>
    /// for local dev — so the guard's job is to fail fast in any deployed environment that
    /// inherits that default without an environment-specific override.
    /// </remarks>
    public static IServiceCollection AddBasketAuthentication(
        this IServiceCollection services,
        ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        services.AddPlatformJwtBearer(options =>
        {
            configuration.Bind(JwtBearerConfigSection, options);
        });

        if (environment.IsDeployedEnvironment())
        {
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .PostConfigure(AssertDeployedJwtBearerOptions);
        }

        services.AddAuthorization();
        services.AddHttpContextAccessor();

        return services;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any of the three strict-validation
    /// flags required in deployed environments has been flipped off. Extracted from the
    /// <c>PostConfigure</c> registration so the security invariant can be unit-tested
    /// without an ASP.NET options pipeline.
    /// </summary>
    internal static void AssertDeployedJwtBearerOptions(JwtBearerOptions options)
    {
        if (!options.TokenValidationParameters.RequireSignedTokens
            || !options.TokenValidationParameters.ValidateIssuerSigningKey
            || !options.RequireHttpsMetadata)
        {
            throw new InvalidOperationException(
                "JWT validation must require signed tokens, validate the signing key, and require " +
                "HTTPS metadata in deployed environments. Check 'Authentication:JwtBearer' " +
                "configuration overrides.");
        }
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}

internal static class AuthenticationConfigSchemes
{
    public const string JwtBearer = JwtBearerDefaults.AuthenticationScheme;
}
