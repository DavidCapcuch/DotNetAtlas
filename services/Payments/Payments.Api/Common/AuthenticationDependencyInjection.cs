using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Payments.Api.Common.Authorization;
using Platform.ServiceDefaults;

namespace Payments.Api.Common;

/// <summary>
/// Authentication + authorization wiring for the Payments API. Mirrors the Ordering
/// precedent (<c>services/Ordering/Ordering.Api/Common/AuthenticationDependencyInjection.cs</c>)
/// trimmed to JWT-only — Payments has no UI surface, only admin-tooling callers
/// carrying a Keycloak access token (ADR-0010).
/// </summary>
internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Registers JWT bearer authentication and the <see cref="AuthPolicies.PaymentsAdmin"/>
    /// policy (admin role + <c>payments.read</c> scope, defense in depth).
    /// </summary>
    public static IServiceCollection AddPaymentsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var isDeployedEnvironment = environment.IsDeployedEnvironment();

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

        // #223: re-pin security-critical TokenValidationParameters AFTER the
        // configuration bind above. PostConfigure runs after every Configure
        // callback (including binders), so a misconfigured appsettings cannot
        // silently disable signed-token / signing-key / issuer / audience /
        // lifetime validation.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.RequireSignedTokens = true;
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

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
