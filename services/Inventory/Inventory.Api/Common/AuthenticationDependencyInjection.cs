using Inventory.Api.Common.Authorization;
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
    /// The deployed-environment JWT hardening — fail-closed at host boot when
    /// <c>RequireHttpsMetadata</c> is off — is owned by the platform
    /// <see cref="JwtBearerConfigurator"/> and applies to every inbound-JWT edge uniformly; there is
    /// no Inventory-specific auth guard (ADR-0009 item 10).
    /// </remarks>
    public static IServiceCollection AddInventoryAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformJwtBearer(options =>
        {
            configuration.Bind(JwtBearerConfigSection, options);
        });

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
