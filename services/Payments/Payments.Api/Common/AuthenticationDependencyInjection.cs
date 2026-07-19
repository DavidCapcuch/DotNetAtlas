using Payments.Api.Common.Authorization;
using Platform.ServiceDefaults.Auth;

namespace Payments.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/> and
    /// registers the <see cref="AuthPolicies.PaymentsAdmin"/> policy (admin role +
    /// <c>payments.read</c> scope, defense in depth). JWT-only — Payments has no UI surface,
    /// only admin-tooling callers carrying a Keycloak access token (ADR-0010). Payments v1
    /// has no outbound HTTP calls (<c>IPaymentGateway</c> is bound to an in-memory stub;
    /// real adapters land in v2), so the outbound service-auth host registration
    /// (<c>AddServiceAuth</c>) is intentionally NOT wired and there is no <c>ServiceAuth</c>
    /// section in <c>appsettings.json</c>. Inbound <c>ValidAudience</c> is set directly under
    /// <c>Authentication:JwtBearer:TokenValidationParameters</c>. When the v2 real adapter
    /// lands, add a <c>ServiceAuth</c> section + <c>services.AddServiceAuth(...)</c> here
    /// in one go.
    /// </summary>
    /// <remarks>
    /// The deployed-environment JWT hardening — fail-closed at host boot when
    /// <c>RequireHttpsMetadata</c> is off — is owned by the platform
    /// <see cref="JwtBearerConfigurator"/> and applies to every inbound-JWT edge uniformly; there is
    /// no Payments-specific auth guard (ADR-0009 item 10).
    /// </remarks>
    public static IServiceCollection AddPaymentsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPlatformJwtBearer(options =>
        {
            configuration.Bind(JwtBearerConfigSection, options);
        });

        // Admin-tooling endpoints: admin role AND payments.read scope (defense in depth).
        // RequireAnyScope adds RequireAuthenticatedUser + the space-separated scope-claim
        // assertion (Platform.ServiceDefaults.Auth, ADR-0010).
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.PaymentsAdmin, policy =>
            {
                policy.RequireRole(Roles.Admin);
                policy.RequireAnyScope(Scopes.PaymentsRead);
            });

        services.AddHttpContextAccessor();

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
