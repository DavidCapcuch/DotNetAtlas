using Platform.ServiceDefaults.Idempotency;

namespace Inventory.Api.Common;

internal static class ApiDependencyInjection
{
    /// <summary>
    /// Wires the presentation layer for Inventory: FastEndpoints + Swagger, CORS,
    /// ProblemDetails, and the idempotency-key output cache (ADR-0013, backed by
    /// <c>redis-cache</c>). Authentication + scope policies live in
    /// <see cref="AuthenticationDependencyInjection"/> and are wired explicitly from
    /// Program.cs.
    /// </summary>
    /// <remarks>
    /// Inventory v1 has no outbound HTTP calls to other services, so the outbound
    /// service-auth host registration (<c>AddServiceAuth</c>) is intentionally NOT
    /// wired in <see cref="AuthenticationDependencyInjection"/>. The
    /// <c>ServiceAuth.ClientId</c> + <c>ClientSecret</c> entries in <c>appsettings.json</c>
    /// are pre-provisioned for the day Inventory grows an outbound HTTP client.
    /// </remarks>
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInventoryFastEndpoints(configuration);

        services.AddInventoryCors(configuration);

        services.AddProblemDetails();

        services.AddIdempotencyKeyOutputCache(configuration, serviceName: "inventory-service");

        services.AddRazorPages();

        return services;
    }
}
