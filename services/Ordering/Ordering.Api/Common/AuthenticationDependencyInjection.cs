using Ordering.Api.Common.Authorization;
using Platform.ServiceDefaults.Auth;

namespace Ordering.Api.Common;

/// <summary>
/// Authentication + authorization wiring for the Ordering API. Mirrors the Catalog
/// precedent (<c>services/Catalog/Catalog.Api/Common/AuthenticationDependencyInjection.cs</c>)
/// — Ordering has no UI surface, so no Cookie/OIDC schemes are needed (ADR-0010:
/// Ordering is invoked via HTTP from the BFF or admin tooling carrying a Keycloak
/// access token). Uses <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/> so the
/// per-environment JWT hardening is centralized; callers override defaults via the
/// <c>Authentication:JwtBearer</c> configuration section.
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
    /// The deployed-environment JWT hardening — fail-closed at host boot when
    /// <c>RequireHttpsMetadata</c> is off — is owned by the platform
    /// <see cref="JwtBearerConfigurator"/> and applies to every inbound-JWT edge uniformly; there is
    /// no Ordering-specific auth guard (ADR-0009 item 10).
    /// </remarks>
    public static IServiceCollection AddOrderingAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPlatformJwtBearer(options =>
        {
            configuration.Bind(JwtBearerConfigSection, options);
        });

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
