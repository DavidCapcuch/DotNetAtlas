using Catalog.Api.Common.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Auth;

namespace Catalog.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/> and
    /// registers the Catalog scope-policy pair (<c>CatalogReadScope</c> / <c>CatalogWriteScope</c>)
    /// per ADR-0010. Catalog v1 has no outbound HTTP calls to other BCs, so the outbound
    /// service-auth host registration (<c>AddServiceAuth</c>) is intentionally not wired and there
    /// is no <c>ServiceAuth</c> section in <c>appsettings.json</c>. When Catalog grows an outbound
    /// BC client, add a <c>ServiceAuth</c> section + <c>services.AddServiceAuth(...)</c> here.
    /// </summary>
    /// <remarks>
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c> and <c>ValidateIssuerSigningKey</c>
    /// remain enabled — protects against a misconfigured env-var silently relaxing JWT validation
    /// in production.
    /// </remarks>
    public static IServiceCollection AddCatalogAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
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

        // Reads are satisfied by either scope (service-to-service); writes require the
        // write scope. RequireAnyScope adds RequireAuthenticatedUser + the space-separated
        // scope-claim assertion (Platform.ServiceDefaults.Auth, ADR-0010).
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.ReadPolicy, policy =>
                policy.RequireAnyScope(Scopes.CatalogRead, Scopes.CatalogWrite))
            .AddPolicy(AuthPolicies.WritePolicy, policy =>
                policy.RequireAnyScope(Scopes.CatalogWrite));

        services.AddHttpContextAccessor();

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
