using FastEndpoints;
using Platform.Api.Swagger;

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
                "Catalog API for DotNet Atlas - Made with ❤️, Powered by ☕");

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
            app.UsePlatformAuthSwaggerGen(app.Configuration);
        }

        return app;
    }
}
