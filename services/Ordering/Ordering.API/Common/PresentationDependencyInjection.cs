using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults.Idempotency;

namespace Ordering.API.Common;

/// <summary>
/// Composition root for the Ordering.API HTTP surface — FastEndpoints,
/// problem-details, the Idempotency-Key output cache (ADR-0013), and the
/// development-time Swagger document.
/// </summary>
internal static class PresentationDependencyInjection
{
    /// <summary>
    /// Service-name token written to the Redis key prefix for the
    /// idempotency-key store (<c>ordering-service:idem:</c>) so multiple
    /// services sharing <c>redis-cache</c> do not collide. Keep in sync with
    /// the Keycloak <c>aud</c> claim and OTEL service-name token.
    /// </summary>
    internal const string ServiceName = "ordering-service";

    internal static IServiceCollection AddPresentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddFastEndpoints()
            .SwaggerDocument(o =>
            {
                o.DocumentSettings = s =>
                {
                    s.Title = "Ordering API";
                    s.Version = "v1";
                };
            });

        services
            .AddProblemDetails();

        // FastEndpoints' .Idempotency() filter is implemented as an
        // IOutputCachePolicy. AddIdempotency wires that policy as a base
        // policy on OutputCacheOptions; AddIdempotencyKeyOutputCache then
        // swaps the in-memory IOutputCacheStore for a Redis-backed one
        // (ADR-0013 line 141; only the Ordering cancel endpoint uses it
        // in v1). Both calls are additive — same AddOutputCache plumbing.
        services.AddIdempotency();
        services.AddIdempotencyKeyOutputCache(configuration, ServiceName);

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

        if (!app.Environment.IsProduction())
        {
            app.UseSwaggerGen();
        }

        return app;
    }
}
