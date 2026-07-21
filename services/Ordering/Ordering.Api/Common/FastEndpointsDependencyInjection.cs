using FastEndpoints;
using Platform.Api.Swagger;
using Platform.ServiceDefaults;

namespace Ordering.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddOrderingFastEndpoints(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddFastEndpoints()
            .AddPlatformAuthSwaggerDocument(
                configuration,
                "Ordering API",
                "v1",
                "Ordering API for DotNet Atlas - Made with ❤️, Powered by ☕\n\n"
                + "Order lifecycle authority. Accepts and orchestrates orders, "
                + "and publishes order events via the transactional outbox.");

        return services;
    }

    internal static WebApplication UseOrderingFastEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseFastEndpoints(config =>
        {
            // Surface validation / error responses as RFC-7807 problem-details.
            config.Errors.UseProblemDetails(detailsConfig =>
            {
                detailsConfig.IndicateErrorCode = true;
                detailsConfig.IndicateErrorSeverity = false;
            });

            // ADR-0012 — versioned routes under /api/v{n}/ordering/...
            // FastEndpoints renders v{Version()} between the prefix and the
            // group route, so a Group("orders") + Version(1) lands on
            // /api/v1/ordering/orders/...
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
