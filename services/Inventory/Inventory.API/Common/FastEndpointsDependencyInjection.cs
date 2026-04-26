using FastEndpoints;
using FastEndpoints.Swagger;

namespace Inventory.API.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddInventoryFastEndpoints(
        this IServiceCollection services)
    {
        services
            .AddFastEndpoints()
            .SwaggerDocument(o =>
            {
                o.MaxEndpointVersion = 1;
                o.DocumentSettings = s =>
                {
                    s.Title = "Inventory API";
                    s.Version = "v1";
                    s.Description =
                        "Event-sourced stock authority with admin-only HTTP surface. " +
                        "Saga lifecycle (Reserve / Confirm / Release) is Kafka-driven; " +
                        "only Receive / Adjust / read endpoints are exposed here.";
                };
            });

        return services;
    }

    internal static WebApplication UseInventoryFastEndpoints(this WebApplication app)
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
