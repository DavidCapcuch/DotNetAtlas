using Inventory.Api.Common.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Auth;

namespace Inventory.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/> and
    /// registers the Inventory scope-policy pair (<c>InventoryReadScope</c> /
    /// <c>InventoryWriteScope</c>) per ADR-0010. Inventory v1 has no outbound HTTP calls so
    /// <c>AddServiceAuth</c> is intentionally not wired and there is no <c>ServiceAuth</c>
    /// section in <c>appsettings.json</c>. Inbound <c>ValidAudience</c> is set directly under
    /// <c>Authentication:JwtBearer:TokenValidationParameters</c>. When Inventory grows an
    /// outbound BC client, add a <c>ServiceAuth</c> section + <c>services.AddServiceAuth(...)</c>
    /// here in one go.
    /// </summary>
    /// <remarks>
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c> and <c>ValidateIssuerSigningKey</c>
    /// remain enabled — protects against a misconfigured env-var silently relaxing JWT validation
    /// in production.
    /// </remarks>
    public static IServiceCollection AddInventoryAuthentication(
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

        // Reads are delegated service-to-service access (scope only); admin-reads add the
        // admin role on top (ops/audit reservation rows carry OrderId, so they are gated
        // tighter than the public stock-availability display reads); writes are human-admin
        // Receive / Adjust hardened with the admin role AND the write scope (defense in depth).
        // A token carrying inventory.write also satisfies the read scope of either read policy.
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.ReadPolicy, policy =>
                policy.RequireAnyScope(Scopes.InventoryRead, Scopes.InventoryWrite))
            .AddPolicy(AuthPolicies.AdminReadPolicy, policy =>
            {
                policy.RequireRole(Roles.Admin);
                policy.RequireAnyScope(Scopes.InventoryRead, Scopes.InventoryWrite);
            })
            .AddPolicy(AuthPolicies.WritePolicy, policy =>
            {
                policy.RequireRole(Roles.Admin);
                policy.RequireAnyScope(Scopes.InventoryWrite);
            });

        services.AddHttpContextAccessor();

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
