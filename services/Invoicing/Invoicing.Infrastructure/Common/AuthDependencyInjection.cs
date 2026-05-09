using Invoicing.Infrastructure.Common.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// Authentication + authorization wiring for the Invoicing API. JWT-only — Invoicing has
/// no UI surface, so no Cookie/OIDC schemes are needed (ADR-0010: invoked via HTTP from
/// the BFF or admin tooling carrying a Keycloak access token).
/// </summary>
public static class AuthDependencyInjection
{
    /// <summary>
    /// Section name read by <see cref="AddInvoicingAuth"/> for the
    /// <see cref="JwtBearerOptions"/> binding — e.g. <c>Authority</c>, <c>Audience</c>,
    /// and <c>TokenValidationParameters</c>.
    /// </summary>
    public const string JwtBearerConfigSection = "Authentication:JwtBearer";

    /// <summary>
    /// Registers JWT bearer authentication and the <see cref="AuthPolicies.InvoicingAdmin"/>
    /// policy. Call from the API composition root (<c>Invoicing.API/Program.cs</c>) before
    /// <c>UseAuthentication</c> / <c>UseAuthorization</c>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Bound to <see cref="JwtBearerConfigSection"/>.</param>
    /// <param name="isDeployedEnvironment">
    /// When <c>true</c>, requires HTTPS metadata on the OIDC discovery doc (production
    /// posture per ADR-0010 § Implementation Notes). Local dev (Keycloak on
    /// <c>http://localhost:9011</c>) needs this off.
    /// </param>
    public static IServiceCollection AddInvoicingAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDeployedEnvironment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                configuration.Bind(JwtBearerConfigSection, options);

                if (isDeployedEnvironment)
                {
                    options.RequireHttpsMetadata = true;
                }
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
}
