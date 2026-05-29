using FastEndpoints;
using Platform.Api.Swagger;

namespace Payments.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddPaymentsFastEndpoints(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddFastEndpoints()
            .AddPlatformAuthSwaggerDocument(
                configuration,
                "Payments API",
                "v1",
                "Payments API for DotNet Atlas - Made with ❤️, Powered by ☕\n\n"
                + "Payment-transaction authority with an admin/internal HTTP surface. "
                + "Payment processing is Kafka-driven (payment-commands); "
                + "publishes transaction events via the transactional outbox.");

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
            app.UsePlatformAuthSwaggerGen();
        }

        return app;
    }
}
