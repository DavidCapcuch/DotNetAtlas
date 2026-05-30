using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ordering.Api.Common.Authorization;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Auth;

namespace Ordering.Api.Common;

/// <summary>
/// Authentication + authorization wiring for the Ordering API. Mirrors the Catalog
/// precedent (<c>services/Catalog/Catalog.Api/Common/AuthenticationDependencyInjection.cs</c>)
/// — Ordering has no UI surface, so no Cookie/OIDC schemes are needed (ADR-0010:
/// Ordering is invoked via HTTP from the BFF or admin tooling carrying a Keycloak
/// access token). Uses <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/> so the
/// per-environment <c>RequireHttpsMetadata</c> toggle is centralized; callers override
/// defaults via the <c>Authentication:JwtBearer</c> configuration section.
/// </summary>
internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Registers JWT bearer authentication (via the platform configurator) and the
    /// <see cref="AuthPolicies.OrderingAdmin"/> policy. Ordering v1 has no outbound HTTP
    /// calls — its saga commands and notifications flow over the Kafka outbox (no service
    /// token) — so the outbound service-auth host registration (<c>AddServiceAuth</c>) is
    /// intentionally not wired and there is no <c>ServiceAuth</c> section in
    /// <c>appsettings.json</c>.
    /// </summary>
    /// <remarks>
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c> and
    /// <c>ValidateIssuerSigningKey</c> remain enabled — protects against a misconfigured
    /// env-var silently relaxing JWT validation in production. HTTPS-metadata gating is
    /// handled by <see cref="JwtBearerConfigurator"/> based on <c>ASPNETCORE_ENVIRONMENT</c>.
    /// </remarks>
    public static IServiceCollection AddOrderingAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

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

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.OrderingAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(Roles.Admin);
            });

        services.AddHttpContextAccessor();

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
