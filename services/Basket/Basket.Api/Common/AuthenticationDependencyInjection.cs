using Platform.ServiceDefaults.Auth;

namespace Basket.Api.Common;

internal static class AuthenticationDependencyInjection
{
    /// <summary>
    /// Configures inbound JWT-bearer authentication via <see cref="JwtBearerConfigurator"/>,
    /// registers the default authorization stack, and wires the outbound service-auth host
    /// registration (ADR-0010) so Basket can call other BCs with a client-credentials token.
    /// Basket is API-only — no Cookie / OIDC schemes (those are Web-UI concerns owned by the BFF).
    /// </summary>
    /// <remarks>
    /// The deployed-environment JWT hardening — fail-closed at host boot when
    /// <c>RequireHttpsMetadata</c> is off — is owned by the platform
    /// <see cref="JwtBearerConfigurator"/> and applies to every inbound-JWT edge uniformly; there is
    /// no Basket-specific auth guard (ADR-0009 item 10).
    /// </remarks>
    public static IServiceCollection AddBasketAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformJwtBearer(options =>
        {
            configuration.Bind(JwtBearerConfigSection, options);
        });

        // Basket uses default JWT-bearer authorization only — every authenticated user operates
        // on their own basket (userId is taken from the sub claim in each endpoint). There are no
        // admin operations, scopes, or roles to enforce, so no custom policies are registered.
        // Contrast: Catalog/Inventory use scope-based policies; Invoicing/Ordering/Payments add
        // role-based admin gates. See Common/Authorization/ in those BCs.
        services.AddAuthorization();
        services.AddHttpContextAccessor();

        services.AddServiceAuth(serviceName: "basket-service");

        return services;
    }

    private const string JwtBearerConfigSection = "Authentication:JwtBearer";
}
