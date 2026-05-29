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
