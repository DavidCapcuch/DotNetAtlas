using DotNetAtlas.Sagas.Common.Observability;
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
using Serilog;
using Serilog.Sinks.OpenTelemetry;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace DotNetAtlas.Sagas.Common;

/// <summary>
/// Dependency injection extensions for observability infrastructure.
/// Configures logging (Serilog) and distributed tracing/metrics (OpenTelemetry).
/// </summary>
public static class ObservabilityDependencyInjection
{
    /// <summary>
    /// Configures Serilog logging with sinks and enrichers.
    /// Sets up console and OpenTelemetry sinks based on the environment.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="isClusterEnvironment">Whether running in a cluster environment.</param>
    /// <returns>The configured web application builder.</returns>
    public static WebApplicationBuilder UseSerilogInternal(
        this WebApplicationBuilder builder,
        bool isClusterEnvironment)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var oltpExporterEndpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

            configuration
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext();
            if (isClusterEnvironment)
            {
                configuration.WriteTo.Console();
            }
            else
            {
                configuration.WriteTo.Console(new ExpressionTemplate(
                    "[{@t:HH:mm:ss} {@l:u3}] " +
                    "[{Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}] " +
                    "{@m}" +
                    "\n" +
                    "{@x}",
                    theme: TemplateTheme.Code));

                if (!string.IsNullOrWhiteSpace(oltpExporterEndpoint))
                {
                    configuration.WriteTo.OpenTelemetry(options =>
                    {
                        options.Endpoint = oltpExporterEndpoint;
                        options.ResourceAttributes = new Dictionary<string, object>
                        {
                            ["service.name"] = ApplicationInfo.AppName
                        };
                        options.IncludedData = IncludedData.SpanIdField | IncludedData.TraceIdField |
                                               IncludedData.SourceContextAttribute;
                    });
                }
            }
        });

        return builder;
    }

    extension(IServiceCollection services)
    {
        public IServiceCollection AddOpenTelemetryInternal(ConfigurationManager configuration)
        {
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
                        .AddSource(SagaInstrumentation.ActivitySourceName)
                        .AddSource(DiagnosticHeaders.DefaultListenerName) // MassTransit ActivitySource
                        .AddEntityFrameworkCoreInstrumentation(options => options.SetDbStatementForText = true)
                        .AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                })
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter(SagaInstrumentation.MeterName)
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
