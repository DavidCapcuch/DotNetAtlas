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
    /// post-configure guard that asserts <c>RequireSignedTokens</c> and
    /// <c>ValidateIssuerSigningKey</c> remain enabled. The <c>configuration.Bind</c> call below
    /// otherwise lets a misconfigured env-var silently relax these flags.
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
                .PostConfigure(options =>
                {
                    if (!options.TokenValidationParameters.RequireSignedTokens
                        || !options.TokenValidationParameters.ValidateIssuerSigningKey)
                    {
                        throw new InvalidOperationException(
                            "JWT validation must require signed tokens and validate the signing " +
                            "key in deployed environments. Check 'Authentication:JwtBearer' " +
                            "configuration overrides.");
                    }
                });
        }

        services.AddAuthorization();
        services.AddHttpContextAccessor();

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}

internal static class AuthenticationConfigSchemes
{
    public const string JwtBearer = JwtBearerDefaults.AuthenticationScheme;
}
