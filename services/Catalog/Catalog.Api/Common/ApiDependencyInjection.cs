using Platform.ServiceDefaults.Idempotency;

namespace Catalog.Api.Common;

internal static class ApiDependencyInjection
{
    /// <summary>
    /// Wires the API layer for Catalog: FastEndpoints + Swagger, CORS, ProblemDetails,
    /// and the idempotency-key output cache (ADR-0013, backed by <c>redis-cache</c>).
    /// Authentication + Catalog scope policies live in
    /// <see cref="AuthenticationDependencyInjection"/> and are wired explicitly from Program.cs.
    /// CORS invariants are enforced at startup by <c>CatalogCorsOptionsValidator</c>.
    /// </summary>
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCatalogFastEndpoints(configuration);

        services.AddCatalogCors(configuration);

        services.AddProblemDetails();

        services.AddIdempotencyKeyOutputCache(configuration, serviceName: "catalog-service");

        services.AddRazorPages();

        return services;
    }
}
