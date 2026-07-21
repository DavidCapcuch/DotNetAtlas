using FastEndpoints;
using Platform.Api.Swagger;
using Platform.ServiceDefaults;

namespace Catalog.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddCatalogFastEndpoints(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddFastEndpoints()
            .AddPlatformAuthSwaggerDocument(
                configuration,
                "Catalog API",
                "v1",
                "Catalog API for DotNet Atlas - Made with ❤️, Powered by ☕\n\n"
                + "Product-information authority. CQRS read-projection backed by Postgres; "
                + "publishes catalog events via the transactional outbox.");

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

        if (!app.Environment.IsDeployedEnvironment())
        {
            app.UsePlatformAuthSwaggerGen();
        }

        return app;
    }
}
