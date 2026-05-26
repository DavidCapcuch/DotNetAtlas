using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ordering.Infrastructure.Common.Authorization;
using Platform.ServiceDefaults.Auth;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// Authentication + authorization wiring for the Ordering API. Mirrors the
/// Catalog precedent (<c>services/Catalog/Catalog.Api/Common/AuthenticationDependencyInjection.cs</c>)
/// — Ordering has no UI surface, so no Cookie/OIDC schemes are needed
/// (ADR-0010: Ordering is invoked via HTTP from the BFF or admin tooling
/// carrying a Keycloak access token). Uses <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/>
/// so the per-environment <c>RequireHttpsMetadata</c> toggle is centralized;
/// callers override defaults via the <see cref="JwtBearerConfigSection"/>
/// configuration section.
/// </summary>
public static class AuthDependencyInjection
{
    /// <summary>
    /// Section name read by <see cref="AddOrderingAuth"/> for the
    /// <see cref="JwtBearerOptions"/> binding — e.g. <c>Authority</c>,
    /// <c>Audience</c>, and <c>TokenValidationParameters</c>.
    /// </summary>
    public const string JwtBearerConfigSection = "Authentication:JwtBearer";

    /// <summary>
    /// Registers JWT bearer authentication (via the platform configurator) and
    /// the <see cref="AuthPolicies.OrderingAdmin"/> policy. Call from the API
    /// composition root (<c>Ordering.Api/Program.cs</c>) before
    /// <c>UseAuthentication</c> / <c>UseAuthorization</c>.
    /// </summary>
    /// <remarks>
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments
    /// a post-configure guard asserts <c>RequireSignedTokens</c> and
    /// <c>ValidateIssuerSigningKey</c> remain enabled — protects against a
    /// misconfigured env-var silently relaxing JWT validation in production.
    /// </remarks>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Bound to <see cref="JwtBearerConfigSection"/>.</param>
    /// <param name="isDeployedEnvironment">
    /// When <c>true</c>, enables the post-configure validation guard above.
    /// HTTPS-metadata gating is handled by <see cref="JwtBearerConfigurator"/>
    /// based on <c>ASPNETCORE_ENVIRONMENT</c>.
    /// </param>
    public static IServiceCollection AddOrderingAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDeployedEnvironment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPlatformJwtBearer(options =>
        {
            configuration.Bind(JwtBearerConfigSection, options);
        });
        services.AddServiceAuth(serviceName: "ordering-service");

        if (isDeployedEnvironment)
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
}
