using FastEndpoints;
using FastEndpoints.Swagger;

namespace Catalog.API.Common;

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

        if (!app.Environment.IsProduction())
        {
            app.UseSwaggerGen();
        }

        return app;
    }
}
