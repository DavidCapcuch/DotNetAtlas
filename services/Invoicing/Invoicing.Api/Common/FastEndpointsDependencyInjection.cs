using FastEndpoints;
using Platform.Api.Swagger;
using Platform.ServiceDefaults;

namespace Invoicing.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddInvoicingFastEndpoints(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddFastEndpoints()
            .AddPlatformAuthSwaggerDocument(
                configuration,
                "Invoicing API",
                "v1",
                "Invoicing API for DotNet Atlas - Made with ❤️, Powered by ☕\n\n"
                + "Invoice authority. Projects Ordering and Payments events into invoices, "
                + "renders PDF documents to blob storage, and notifies buyers via email and the buyer portal.");

        return services;
    }

    internal static WebApplication UseInvoicingFastEndpoints(this WebApplication app)
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

            // ADR-0012 — versioned routes under /api/v{n}/invoicing/...
            // FastEndpoints renders v{Version()} between the prefix and the group route,
            // so a Group("invoicing/invoices") + Version(1) lands on
            // /api/v1/invoicing/invoices/...
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
