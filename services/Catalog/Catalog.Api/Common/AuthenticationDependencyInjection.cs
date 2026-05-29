using Catalog.Api.Common.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Auth;

namespace Catalog.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/>,
    /// registers the Catalog scope-policy pair (<c>CatalogReadScope</c> / <c>CatalogWriteScope</c>)
    /// per ADR-0010, and wires the outbound service-auth host registration so Catalog can call
    /// other BCs with a client-credentials token (registered for symmetry even though Catalog has
    /// no outbound BC calls today).
    /// </summary>
    /// <remarks>
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c> and <c>ValidateIssuerSigningKey</c>
    /// remain enabled — protects against a misconfigured env-var silently relaxing JWT validation
    /// in production.
    /// </remarks>
    public static IServiceCollection AddCatalogAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
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
            .AddPolicy(AuthPolicies.ReadPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => HasAnyScope(ctx, Scopes.CatalogRead, Scopes.CatalogWrite));
            })
            .AddPolicy(AuthPolicies.WritePolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => HasAnyScope(ctx, Scopes.CatalogWrite));
            });

        services.AddHttpContextAccessor();

        services.AddServiceAuth(serviceName: "catalog-service");

        return services;
    }

    // Keycloak emits scopes as a single space-separated `scope` claim (RFC 6749).
    // Read is implied by write; write requires the write scope.
    private static bool HasAnyScope(AuthorizationHandlerContext ctx, params string[] required)
    {
        foreach (var claim in ctx.User.FindAll("scope"))
        {
            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var needle in required)
                {
                    if (string.Equals(scope, needle, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
