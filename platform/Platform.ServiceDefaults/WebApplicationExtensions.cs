using System.Text.Json;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using Prometheus;

namespace Platform.ServiceDefaults;

/// <summary>
/// Extension methods for WebApplication host wiring — health-check endpoints and the
/// environment-gated exception surface.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Wires the environment-appropriate exception surface. Deployed tiers
    /// (<see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/>) get the platform
    /// <c>UseExceptionHandler</c> — a redacted ProblemDetails response via the registered
    /// exception handler / <c>IProblemDetailsService</c>. Developer tiers (Development/Testing) get
    /// the developer exception page with full diagnostics. Gated on <c>IsDeployedEnvironment()</c>
    /// so a stack-trace page can never ship to a deployed cluster — the same deployed redaction the
    /// platform exception handler and health-check response writer already apply.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UsePlatformExceptionHandling(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDeployedEnvironment())
        {
            app.UseExceptionHandler();
        }
        else
        {
            app.UseDeveloperExceptionPage();
        }

        return app;
    }

    /// <summary>
    /// Maps health check endpoints with appropriate filters.
    /// Maps liveness endpoint at /api/healthz and readiness endpoint at /api/readiness.
    /// </summary>
    /// <remarks>
    /// The response body is environment-aware. In developer and test environments
    /// (<see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> is <c>false</c>) the verbose
    /// <see cref="UIResponseWriter"/> is used — it lists every check's name, duration, and, on
    /// failure, the exception message, which is invaluable when debugging locally. In deployed
    /// environments (Staging/Production/…) a minimal writer emits only the overall status
    /// (<c>{"status":"Healthy"}</c>): the verbose body would otherwise disclose internal topology
    /// (redis-cache, Kafka, DB names) and raw exception text on these unauthenticated endpoints.
    /// Orchestrator readiness gating is unaffected — the 200/503 status code is set by the
    /// middleware's <see cref="HealthCheckOptions.ResultStatusCodes"/>, independently of the body —
    /// and per-check detail still reaches the monitoring stack via the Prometheus exporter
    /// (<see cref="UsePlatformHealthChecksPrometheusExporter"/> at <c>/api/health/prometheus</c>),
    /// which is the appropriate internal-only channel.
    /// </remarks>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapPlatformHealthCheckEndpoints(this WebApplication app)
    {
        Func<HttpContext, HealthReport, Task> responseWriter = app.Environment.IsDeployedEnvironment()
            ? WriteOverallStatusOnly
            : UIResponseWriter.WriteHealthCheckUIResponse;

        // Map readiness endpoint (includes database, external APIs, messaging checks)
        app.MapHealthChecks(ServiceDefaultHealthCheckTags.ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(ServiceDefaultHealthCheckTags.ReadinessTag),
            ResponseWriter = responseWriter
        }).ShortCircuit();

        // Map liveness endpoint (basic health check)
        app.MapHealthChecks(ServiceDefaultHealthCheckTags.HealthEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(ServiceDefaultHealthCheckTags.LivenessTag),
            ResponseWriter = responseWriter
        }).ShortCircuit();

        return app;
    }

    /// <summary>
    /// Minimal health-check response writer for deployed environments: emits only the aggregate
    /// status as JSON (<c>{"status":"Healthy|Degraded|Unhealthy"}</c>) with no per-check names,
    /// durations, or exception detail, so the unauthenticated health endpoints do not leak internal
    /// dependency topology or raw exception text. The HTTP status code (200 vs 503) is set
    /// independently by the health-check middleware, so readiness gating is unaffected.
    /// </summary>
    private static Task WriteOverallStatusOnly(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(new { status = report.Status.ToString() }));
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
