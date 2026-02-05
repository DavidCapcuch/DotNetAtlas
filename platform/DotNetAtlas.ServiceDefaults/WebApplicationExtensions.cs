using DotNetAtlas.ServiceDefaults.Config;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;

namespace DotNetAtlas.ServiceDefaults;

/// <summary>
/// Extension methods for WebApplication to configure health check endpoints.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps health check endpoints with appropriate filters.
    /// Maps liveness endpoint at /api/healthz and readiness endpoint at /api/readiness.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapPlatformHealthCheckEndpoints(this WebApplication app)
    {
        // Map readiness endpoint (includes database, external APIs, messaging checks)
        app.MapHealthChecks(ServiceDefaultHealthCheckTags.ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).ShortCircuit();

        // Map liveness endpoint (basic health check)
        app.MapHealthChecks(ServiceDefaultHealthCheckTags.HealthEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(ServiceDefaultHealthCheckTags.LivenessTag),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).ShortCircuit();

        return app;
    }

    /// <summary>
    /// Configures Prometheus health check metrics exporter.
    /// Suppresses default prometheus-net collectors and returns 200 for all health states.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UsePlatformHealthChecksPrometheusExporter(this WebApplication app)
    {
        // Suppress default prometheus-net collectors and collect only health-related metrics to avoid duplicated scraping.
        // As of now, there is no standardized way to push health metrics through OTEL Collector
        // all other collected metrics are unaffected and still exported through OTEL Collector to prometheus.
        Metrics.SuppressDefaultMetrics();

        app.UseHealthChecksPrometheusExporter(ServiceDefaultHealthCheckTags.PrometheusEndpointPath, options =>
        {
            options.Predicate = healthCheck => healthCheck.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag);
            options.ResultStatusCodes = new Dictionary<HealthStatus, int>
            {
                // Prometheus expects 200 also for degraded state, otherwise throws in the scrape job
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status200OK
            };
        });

        return app;
    }
}
