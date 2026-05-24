using MassTransit;
using MassTransit.Logging;
using MassTransit.Monitoring;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Platform.ServiceDefaults.Pii;
using SagaOrchestrators.Common.Observability;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Observability;

namespace SagaOrchestrators.Common;

/// <summary>
/// Dependency injection extensions for observability infrastructure.
/// Configures logging (Serilog) and distributed tracing/metrics (OpenTelemetry).
/// </summary>
public static class ObservabilityDependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddOpenTelemetryInternal(ConfigurationManager configuration)
        {
            // Be careful of ENV variables overriding what is set in appsettings.json for otel collector
            // OTEL_EXPORTER_OTLP_ENDPOINT is standardized can be set as ENV e.g., by Rider OpenTelemetry plugin
            var oltpExporterEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            if (string.IsNullOrWhiteSpace(oltpExporterEndpoint))
            {
                return services;
            }

            var serviceName = configuration["OTEL_SERVICE_NAME"] ?? ApplicationInfo.AppName;

            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: serviceName,
                        serviceVersion: ApplicationInfo.Version)
                    .AddContainerDetector()
                    .AddHostDetector())
                .WithTracing(tracing =>
                {
                    tracing.AddSource("*")
                        .AddSource(SagaActivitySource.ActivitySourceName)
                        .AddSource(DiagnosticHeaders.DefaultListenerName) // MassTransit ActivitySource
                        .AddEntityFrameworkCoreInstrumentation()
                        .AddPiiRedactionProcessor() // ADR-0011 — redacts [Pii]-tagged span attributes before export
                        .AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                })
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter(ApplicationInfo.AppName)
                        .AddMeter(InstrumentationOptions.MeterName) // MassTransit Meter
                        .AddRuntimeInstrumentation()
                        .AddProcessInstrumentation()
                        .AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                });

            return services;
        }

        public IServiceCollection AddSagaStateObservability()
        {
            services.AddStateObserver<PaymentProcessingSagaState, PaymentSagaStateObserver>();

            return services;
        }
    }
}
