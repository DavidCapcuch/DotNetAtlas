using Inventory.Api.Common.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Auth;

namespace Inventory.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/> and
    /// allows callers to override defaults through the <c>Authentication:JwtBearer</c>
    /// configuration section. Registers the Inventory scope-policy pair
    /// (<c>InventoryReadScope</c> / <c>InventoryCommandsScope</c>) per ADR-0010.
    /// </summary>
    /// <remarks>
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c> and <c>ValidateIssuerSigningKey</c>
    /// remain enabled — protects against a misconfigured env-var silently relaxing JWT validation
    /// in production.
    /// </remarks>
    public static IServiceCollection AddInventoryAuthentication(
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

        services.AddAuthorization(options => options.AddInventoryScopePolicies());
        services.AddHttpContextAccessor();

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
