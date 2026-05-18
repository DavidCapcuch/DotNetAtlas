using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Idempotency;

namespace Invoicing.API.Common;

/// <summary>
/// Composition root for the Invoicing.API HTTP surface — FastEndpoints, problem-details,
/// the Idempotency-Key output cache (ADR-0013), and the development-time Swagger document.
/// </summary>
internal static class PresentationDependencyInjection
{
    /// <summary>
    /// Service-name token written to the Redis key prefix for the idempotency-key store
    /// (<c>invoicing-service:idem:</c>) so multiple services sharing <c>redis-cache</c>
    /// do not collide. Keep in sync with the Keycloak <c>aud</c> claim and OTel service-name
    /// token.
    /// </summary>
    internal const string ServiceName = "invoicing-service";

    internal static IServiceCollection AddPresentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddFastEndpoints()
            .SwaggerDocument(o =>
            {
                // ADR-0012: every endpoint declares Version(1); without an explicit cap,
                // FastEndpoints excludes all versioned endpoints from the OpenAPI doc
                // (default MaxEndpointVersion = 0), leaving `paths` empty.
                o.MaxEndpointVersion = 1;
                o.DocumentSettings = s =>
                {
                    s.Title = "Invoicing API";
                    s.Version = "v1";
                };
            });

        services.AddProblemDetails();

        // FastEndpoints' .Idempotency() filter is implemented as an IOutputCachePolicy.
        // AddIdempotency wires that policy as a base policy on OutputCacheOptions;
        // AddIdempotencyKeyOutputCache then swaps the in-memory IOutputCacheStore for a
        // Redis-backed one (ADR-0013). v1 only the resend endpoint uses .Idempotency();
        // both calls are additive over the same AddOutputCache plumbing.
        services.AddIdempotency();
        services.AddIdempotencyKeyOutputCache(configuration, ServiceName);

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

        if (!app.Environment.IsProduction())
        {
            app.UseSwaggerGen();
        }

        return app;
    }
}
