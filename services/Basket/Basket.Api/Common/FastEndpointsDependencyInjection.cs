using FastEndpoints;
using FastEndpoints.Swagger;

namespace Basket.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddBasketFastEndpoints(
        this IServiceCollection services)
    {
        services
            .AddFastEndpoints()
            .SwaggerDocument(o =>
            {
                o.MaxEndpointVersion = 1;
                o.DocumentSettings = s =>
                {
                    s.Title = "Basket API";
                    s.Version = "v1";
                    s.Description =
                        "Redis-backed basket aggregate with anti-corruption layer to Catalog. ";
                };
            });

        return services;
    }

    internal static WebApplication UseBasketFastEndpoints(this WebApplication app)
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
