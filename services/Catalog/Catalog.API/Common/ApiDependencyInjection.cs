using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults.Auth;
using Platform.ServiceDefaults.Idempotency;

namespace Catalog.API.Common;

internal static class ApiDependencyInjection
{
    /// <summary>
    /// Wires the presentation layer for Catalog: FastEndpoints + Swagger, JWT bearer auth +
    /// Catalog scope policies (ADR-0010), CORS, ProblemDetails, the idempotency-key output cache
    /// (ADR-0013, backed by <c>redis-cache</c>), and the outbound service-auth host registration
    /// (ADR-0010 — Catalog has no outbound BC calls today, registered for symmetry with
    /// Basket/Weather defaults).
    /// </summary>
    public static IServiceCollection AddPresentation(
        this IServiceCollection services,
        ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        services.AddCatalogFastEndpoints();

        services.AddCatalogCors(configuration);

        services.AddProblemDetails();

        services.AddCatalogAuthentication(configuration, environment);

        services.AddServiceAuth(serviceName: "catalog");

        services.AddIdempotencyKeyOutputCache(configuration, serviceName: "catalog");

        return services;
    }
}
