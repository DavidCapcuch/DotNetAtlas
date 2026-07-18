using Catalog.Api.Common.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults;
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
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c>, <c>ValidateIssuerSigningKey</c>,
    /// and <c>RequireHttpsMetadata</c> remain enabled. The <c>configuration.Bind</c> call below
    /// otherwise lets a misconfigured env-var silently relax these flags, and
    /// <c>appsettings.json</c> ships <c>RequireHttpsMetadata: false</c> for local dev — so the
    /// guard's job is to fail fast in any deployed environment that inherits that default without
    /// an environment-specific override.
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
                .PostConfigure(AssertDeployedJwtBearerOptions);
        }

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

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any of the three strict-validation
    /// flags required in deployed environments has been flipped off. Extracted from the
    /// <c>PostConfigure</c> registration so the security invariant can be unit-tested
    /// without an ASP.NET options pipeline.
    /// </summary>
    internal static void AssertDeployedJwtBearerOptions(JwtBearerOptions options)
    {
        if (!options.TokenValidationParameters.RequireSignedTokens
            || !options.TokenValidationParameters.ValidateIssuerSigningKey
            || !options.RequireHttpsMetadata)
        {
            throw new InvalidOperationException(
                "JWT validation must require signed tokens, validate the signing key, and require " +
                "HTTPS metadata in deployed environments. Check 'Authentication:JwtBearer' " +
                "configuration overrides.");
        }
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
