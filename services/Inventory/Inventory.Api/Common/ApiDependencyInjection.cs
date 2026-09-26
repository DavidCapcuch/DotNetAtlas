using FastEndpoints;
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
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInventoryFastEndpoints(configuration);

        services.AddInventoryCors(configuration);

        services.AddProblemDetails();

        // AddIdempotency registers the output-cache policy FastEndpoints' .Idempotency() runs on —
        // without it that endpoint option is inert and a retry re-runs the handler.
        services.AddIdempotency();
        services.AddIdempotencyKeyOutputCache(configuration, serviceName: "inventory-service");

        services.AddRazorPages();

        return services;
    }
}
