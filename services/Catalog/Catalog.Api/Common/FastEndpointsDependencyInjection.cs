using FastEndpoints;
using FastEndpoints.Swagger;

namespace Catalog.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddCatalogFastEndpoints(
        this IServiceCollection services)
    {
        services
            .AddFastEndpoints()
            .SwaggerDocument(o =>
            {
                o.MaxEndpointVersion = 1;
                o.DocumentSettings = s =>
                {
                    s.Title = "Catalog API";
                    s.Version = "v1";
                    s.Description =
                        "Product-information authority. CQRS read-projection backed by Postgres; " +
                        "publishes catalog events via the transactional outbox.";
                };
            });

        return services;
    }

    internal static WebApplication UseCatalogFastEndpoints(this WebApplication app)
    {
        app.UseFastEndpoints(config =>
        {
            config.Errors.UseProblemDetails(detailsConfig =>
            {
                detailsConfig.IndicateErrorCode = true;
                detailsConfig.IndicateErrorSeverity = false;
            });

            config.Versioning.Prefix = "v";
            config.Versioning.PrependToRoute = true;
            config.Versioning.DefaultVersion = 1;
            config.Endpoints.RoutePrefix = "api";
        });

        // Swagger UI is exposed only on the developer tier (laptop dotnet run +
        // docker-compose). Any deployed cluster (Dev / Staging / Production)
        // suppresses it — under the post-#213 env taxonomy, "Development" is the
        // sole developer-facing environment name, so gating on IsDevelopment()
        // (rather than the broader !IsProduction()) prevents Swagger from leaking
        // schema / endpoint details in non-prod deployed clusters.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerGen();
        }

        return app;
    }
}
