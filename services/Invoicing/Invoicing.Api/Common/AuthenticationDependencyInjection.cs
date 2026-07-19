using Invoicing.Api.Common.Authorization;
using Platform.ServiceDefaults.Auth;

namespace Invoicing.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/> and
    /// registers the <see cref="AuthPolicies.InvoicingAdmin"/> policy. JWT-only — Invoicing has
    /// no UI surface (ADR-0010: invoked via HTTP from the BFF or admin tooling carrying a
    /// Keycloak access token). Invoicing v1 has no outbound HTTP calls to other services (only
    /// the Azure Blob SDK), so the outbound service-auth host registration
    /// (<c>AddServiceAuth</c>) is intentionally NOT wired and there is no <c>ServiceAuth</c>
    /// section in <c>appsettings.json</c>. Inbound <c>ValidAudience</c> is set directly under
    /// <c>Authentication:JwtBearer:TokenValidationParameters</c>. When Invoicing grows an
    /// outbound BC client, add a <c>ServiceAuth</c> section + <c>services.AddServiceAuth(...)</c>
    /// here in one go.
    /// </summary>
    /// <remarks>
    /// The deployed-environment JWT hardening — fail-closed at host boot when
    /// <c>RequireHttpsMetadata</c> is off — is owned by the platform
    /// <see cref="JwtBearerConfigurator"/> and applies to every inbound-JWT edge uniformly; there is
    /// no Invoicing-specific auth guard (ADR-0009 item 10).
    /// </remarks>
    public static IServiceCollection AddInvoicingAuthentication(
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
            .AddPolicy(AuthPolicies.InvoicingAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(Roles.Admin);
            });

        services.AddHttpContextAccessor();

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
