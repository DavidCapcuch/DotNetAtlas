using Invoicing.Api.Common.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults;
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
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c> and <c>ValidateIssuerSigningKey</c>
    /// remain enabled — protects against a misconfigured env-var silently relaxing JWT validation
    /// in production.
    /// </remarks>
    public static IServiceCollection AddInvoicingAuthentication(
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
