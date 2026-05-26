using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payments.Api.Common.Authorization;
using Platform.ServiceDefaults;
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
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c> and <c>ValidateIssuerSigningKey</c>
    /// remain enabled — protects against a misconfigured env-var silently relaxing JWT validation
    /// in production.
    /// </remarks>
    public static IServiceCollection AddPaymentsAuthentication(
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

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
