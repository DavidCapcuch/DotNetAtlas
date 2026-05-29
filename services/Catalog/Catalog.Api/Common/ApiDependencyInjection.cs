using Platform.ServiceDefaults.Idempotency;

namespace Catalog.Api.Common;

internal static class ApiDependencyInjection
{
    /// <summary>
    /// Wires the presentation layer for Catalog: FastEndpoints + Swagger, CORS, ProblemDetails,
    /// and the idempotency-key output cache (ADR-0013, backed by <c>redis-cache</c>).
    /// Authentication + Catalog scope policies + the outbound service-auth host registration
    /// live in <see cref="AuthenticationDependencyInjection"/> and are wired explicitly from
    /// Program.cs.
    /// </summary>
    public static IServiceCollection AddPresentation(
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
