using FastEndpoints;
using FastEndpoints.Swagger;

namespace Payments.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddPaymentsFastEndpoints(this IServiceCollection services)
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
                    s.Title = "Payments API";
                    s.Version = "v1";
                };
            });

        return services;
    }

    internal static WebApplication UsePaymentsFastEndpoints(this WebApplication app)
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

            // ADR-0012 — versioned routes under /api/v{n}/payments/...
            // FastEndpoints renders v{Version()} between the prefix and the
            // group route, so a Group("payments") + Version(1) lands on
            // /api/v1/payments/...
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
