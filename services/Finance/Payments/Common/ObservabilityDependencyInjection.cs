using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Payments.Common.Observability;

namespace Payments.Common;

/// <summary>
/// Dependency injection extensions for observability infrastructure.
/// Configures distributed tracing/metrics (OpenTelemetry).
/// </summary>
public static class ObservabilityDependencyInjection
{
    /// <summary>
    /// Configures OpenTelemetry distributed tracing and metrics.
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
            var otel = services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: ApplicationInfo.AppName, serviceVersion: ApplicationInfo.Version)
                    .AddContainerDetector()
                    .AddHostDetector())
                .WithTracing(tracing =>
                {
                    tracing
                        .AddEntityFrameworkCoreInstrumentation(options => options.SetDbStatementForText = true)
                        .AddSource("*");

                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                })
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter("*")
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
