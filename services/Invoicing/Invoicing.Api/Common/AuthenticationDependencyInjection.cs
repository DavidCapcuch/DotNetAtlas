using Invoicing.Api.Common.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults;

namespace Invoicing.Api.Common;

/// <summary>
/// Authentication + authorization wiring for the Invoicing API. JWT-only — Invoicing has
/// no UI surface, so no Cookie/OIDC schemes are needed (ADR-0010: invoked via HTTP from
/// the BFF or admin tooling carrying a Keycloak access token).
/// </summary>
internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Registers JWT bearer authentication and the <see cref="AuthPolicies.InvoicingAdmin"/>
    /// policy. Call from the API composition root (<c>Invoicing.Api/Program.cs</c>) before
    /// <c>UseAuthentication</c> / <c>UseAuthorization</c>.
    /// </summary>
    public static IServiceCollection AddInvoicingAuthentication(
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
