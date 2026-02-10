using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability;
using MassTransit;
using MassTransit.Logging;
using MassTransit.Monitoring;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DotNetAtlas.Sagas.Common;

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

            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: ApplicationInfo.AppName,
                        serviceVersion: ApplicationInfo.Version)
                    .AddContainerDetector()
                    .AddHostDetector())
                .WithTracing(tracing =>
                {
                    tracing.AddSource("*")
                        .AddSource(SagaActivitySource.ActivitySourceName)
                        .AddSource(DiagnosticHeaders.DefaultListenerName) // MassTransit ActivitySource
                        .AddEntityFrameworkCoreInstrumentation(options => options.SetDbStatementForText = true)
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
            services.AddStateObserver<AlertSubscriptionPurchaseSagaState, AlertSubscriptionSagaStateObserver>();
            services.AddStateObserver<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionSagaStateObserver>();
            services.AddStateObserver<PaymentProcessingSagaState, PaymentSagaStateObserver>();

            return services;
        }
    }
}
