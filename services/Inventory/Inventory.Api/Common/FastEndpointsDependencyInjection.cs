using FastEndpoints;
using Platform.Api.Swagger;
using Platform.ServiceDefaults;

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
                "Inventory API for DotNet Atlas - Made with ❤️, Powered by ☕\n\n"
                + "Event-sourced stock authority with admin-only HTTP surface. "
                + "Saga lifecycle (Reserve / Confirm / Release) is Kafka-driven; "
                + "only Receive / Adjust / read endpoints are exposed here.");

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

        if (!app.Environment.IsDeployedEnvironment())
        {
            app.UsePlatformAuthSwaggerGen();
        }

        return app;
    }
}
