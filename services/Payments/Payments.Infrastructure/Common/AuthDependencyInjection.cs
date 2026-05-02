using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Infrastructure.Common.Authorization;

namespace Payments.Infrastructure.Common;

/// <summary>
/// Authentication + authorization wiring for the Payments API. Mirrors the
/// Ordering precedent (<c>services/Ordering/Ordering.Infrastructure/Common/AuthDependencyInjection.cs</c>)
/// trimmed to JWT-only — Payments has no UI surface, only admin-tooling
/// callers carrying a Keycloak access token (ADR-0010).
/// </summary>
public static class AuthDependencyInjection
{
    /// <summary>
    /// Section name read by <see cref="AddPaymentsAuth"/> for the
    /// <see cref="JwtBearerOptions"/> binding —
    /// <c>Authority</c>, <c>Audience</c>, and <c>TokenValidationParameters</c>.
    /// </summary>
    public const string JwtBearerConfigSection = "Authentication:JwtBearer";

    /// <summary>
    /// Registers JWT bearer authentication and the
    /// <see cref="AuthPolicies.PaymentsAdmin"/> policy. Call from the API
    /// composition root (<c>Payments.Api/Program.cs</c>) before
    /// <c>UseAuthentication</c> / <c>UseAuthorization</c>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Bound to <see cref="JwtBearerConfigSection"/>.</param>
    /// <param name="isDeployedEnvironment">
    /// When <c>true</c>, requires HTTPS metadata on the OIDC discovery doc
    /// (production posture per ADR-0010 § Implementation Notes). Local dev
    /// (Keycloak on http://localhost:9011) needs this off.
    /// </param>
    public static IServiceCollection AddPaymentsAuth(
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
            .AddPolicy(AuthPolicies.PaymentsAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(Roles.Admin);

                // OAuth2 emits scopes either as a single space-separated `scope`
                // claim (Keycloak default) or as multiple `scope` claims (RFC 8693
                // styled servers). Match either shape.
                policy.RequireAssertion(ctx =>
                {
                    foreach (var claim in ctx.User.FindAll("scope"))
                    {
                        var scopes = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var scope in scopes)
                        {
                            if (string.Equals(scope, Scopes.PaymentsRead, StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                });
            });

        services.AddHttpContextAccessor();

        return services;
    }
}
