using Catalog.Api.Common.Authorization;
using Platform.ServiceDefaults.Auth;

namespace Catalog.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/> and
    /// registers the Catalog scope-policy pair (<c>CatalogReadScope</c> / <c>CatalogWriteScope</c>)
    /// per ADR-0010. Catalog v1 has no outbound HTTP calls to other BCs, so the outbound
    /// service-auth host registration (<c>AddServiceAuth</c>) is intentionally not wired and there
    /// is no <c>ServiceAuth</c> section in <c>appsettings.json</c>. When Catalog grows an outbound
    /// BC client, add a <c>ServiceAuth</c> section + <c>services.AddServiceAuth(...)</c> here.
    /// </summary>
    /// <remarks>
    /// The deployed-environment JWT hardening — fail-closed at host boot when
    /// <c>RequireHttpsMetadata</c> is off — is owned by the platform
    /// <see cref="JwtBearerConfigurator"/> and applies to every inbound-JWT edge uniformly; there is
    /// no Catalog-specific auth guard (ADR-0009 item 10).
    /// </remarks>
    public static IServiceCollection AddCatalogAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformJwtBearer(options =>
        {
            configuration.Bind(JwtBearerConfigSection, options);
        });

        // Reads are delegated service-to-service access (scope only); writes are
        // human-admin product / category mutations hardened with the admin role AND
        // the write scope (defense in depth). A token carrying catalog.write also
        // satisfies the read policy. RequireAnyScope adds RequireAuthenticatedUser +
        // the space-separated scope-claim assertion (Platform.ServiceDefaults.Auth, ADR-0010).
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.ReadPolicy, policy =>
                policy.RequireAnyScope(Scopes.CatalogRead, Scopes.CatalogWrite))
            .AddPolicy(AuthPolicies.WritePolicy, policy =>
            {
                policy.RequireRole(Roles.Admin);
                policy.RequireAnyScope(Scopes.CatalogWrite);
            });

        services.AddHttpContextAccessor();

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
