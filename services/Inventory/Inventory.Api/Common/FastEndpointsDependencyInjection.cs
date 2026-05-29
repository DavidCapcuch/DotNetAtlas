using FastEndpoints;
using Platform.Api.Swagger;

namespace Inventory.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddInventoryFastEndpoints(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddFastEndpoints()
            .AddPlatformAuthSwaggerDocument(
                configuration,
                "Inventory API",
                "v1",
                "Inventory API for DotNet Atlas - Made with ❤️, Powered by ☕");

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
            app.UsePlatformAuthSwaggerGen(app.Configuration);
        }

        return app;
    }
}
