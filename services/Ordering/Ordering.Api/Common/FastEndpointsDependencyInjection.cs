using FastEndpoints;
using FastEndpoints.Swagger;

namespace Ordering.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddOrderingFastEndpoints(this IServiceCollection services)
    {
        services.AddFastEndpoints()
            .SwaggerDocument(o =>
            {
                // ADR-0012: every endpoint declares Version(1); without an explicit cap,
                // FastEndpoints excludes all versioned endpoints from the OpenAPI doc
                // (default MaxEndpointVersion = 0), leaving `paths` empty.
                o.MaxEndpointVersion = 1;
                o.DocumentSettings = s =>
                {
                    s.Title = "Ordering API";
                    s.Version = "v1";
                };
            });

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
