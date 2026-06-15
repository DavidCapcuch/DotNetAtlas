using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Ordering.Infrastructure.Common.Observability;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Pii;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for observability infrastructure.
/// Configures distributed tracing/metrics (OpenTelemetry).
/// </summary>
public static class ObservabilityDependencyInjection
{
    /// <summary>
    /// Configures OpenTelemetry distributed tracing and metrics. Wires
    /// AspNetCore + HttpClient + EF Core instrumentation; skips
    /// Redis/FusionCache/SignalR/Hangfire because Ordering doesn't use
    /// them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="isDeployedEnvironment">Whether running in a deployed environment.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOpenTelemetry(
        this IServiceCollection services,
        bool isDeployedEnvironment,
        ConfigurationManager configuration)
    {
        services.AddMetrics();

        // Be careful of ENV variables overriding what is set in appsettings.json for otel collector
        // OTEL_EXPORTER_OTLP_ENDPOINT is standardized can be set as ENV e.g., by Rider OpenTelemetry plugin
        var oltpExporterEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(oltpExporterEndpoint))
        {
            var serviceName = configuration["OTEL_SERVICE_NAME"] ?? ApplicationInfo.AppName;

            var otel = services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: serviceName, serviceVersion: ApplicationInfo.Version)
                    .AddContainerDetector()
                    .AddHostDetector())
                .WithTracing(tracing =>
                {
                    tracing.AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = false; // handled in tracing behavior
                            options.Filter = context =>
                                !context.Request.Path.StartsWithSegments(
                                    ServiceDefaultHealthCheckTags.HealthEndpointPath,
                                    StringComparison.OrdinalIgnoreCase)
                                && !context.Request.Path.StartsWithSegments(
                                    ServiceDefaultHealthCheckTags.ReadinessEndpointPath,
                                    StringComparison.OrdinalIgnoreCase);
                        })
                        .AddHttpClientInstrumentation()
                        .AddEntityFrameworkCoreInstrumentation()
                        .AddSource("*")
                        .AddPiiRedactionProcessor(); // ADR-0011 — redacts [Pii]-tagged span attributes before export

                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                })
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter("*")
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddProcessInstrumentation();

                    metrics.SetExemplarFilter(isDeployedEnvironment
                        ? ExemplarFilterType.TraceBased
                        : ExemplarFilterType.AlwaysOn);

                    metrics.AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                });
        }

        return services;
    }
}
