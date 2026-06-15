using FastEndpoints;
using Platform.Api.Swagger;

namespace EShop.BFF.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddBffFastEndpoints(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddFastEndpoints()
            .AddPlatformAuthSwaggerDocument(
                configuration,
                "BFF API",
                "v1",
                "Backend-for-Frontend aggregation API for DotNet Atlas - Made with ❤️, Powered by ☕\n\n"
                + "Composes the internal services (Catalog + Inventory today; Basket + Ordering in later "
                + "slices) into client-facing pages with edge caching + resilience. Read-only and public "
                + "in this slice (bff.md § 3.1).");

        return services;
    }

    internal static WebApplication UseBffFastEndpoints(this WebApplication app)
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
            app.UsePlatformAuthSwaggerGen();
        }

        return app;
    }
}
