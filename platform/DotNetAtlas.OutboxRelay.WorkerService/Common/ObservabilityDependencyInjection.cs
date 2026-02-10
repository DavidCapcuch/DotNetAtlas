using DotNetAtlas.OutboxRelay.WorkerService.Observability;
using DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DotNetAtlas.OutboxRelay.WorkerService.Common;

/// <summary>
/// Dependency injection extensions for observability infrastructure.
/// Configures logging (Serilog) and distributed tracing/metrics (OpenTelemetry).
/// </summary>
public static class ObservabilityDependencyInjection
{
    public static IServiceCollection AddOpenTelemetryInternal(
        this IServiceCollection services,
        bool isDeployedEnvironment,
        ConfigurationManager configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMetrics();

        services.AddOptionsWithValidateOnStart<OutboxMetricsCollectorOptions>()
            .BindConfiguration(OutboxMetricsCollectorOptions.Section)
            .ValidateDataAnnotations();
        services.AddHostedService<OutboxMetricsCollector>();
        services.AddSingleton<OutboxRelayMetrics>();

        // Be careful of ENV variables overriding what is set in appsettings.json for otel collector
        // OTEL_EXPORTER_OTLP_ENDPOINT is standardized can be set as ENV e.g., by Rider OpenTelemetry plugin
        var oltpExporterEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(oltpExporterEndpoint))
        {
            return services;
        }

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: ApplicationInfo.AppName,
                    serviceVersion: ApplicationInfo.Version)
                .AddContainerDetector()
                .AddHostDetector())
            .WithTracing(tracing =>
            {
                tracing.AddSource("*");

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

        return services;
    }
}
