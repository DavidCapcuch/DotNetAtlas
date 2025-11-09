using DotNetAtlas.OutboxRelay.WorkerService.Observability;
using DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.OpenTelemetry;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace DotNetAtlas.OutboxRelay.WorkerService.Common;

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
                            ["service.name"] = OutboxRelayInstrumentation.AppName
                        };
                        options.IncludedData = IncludedData.SpanIdField | IncludedData.TraceIdField |
                                               IncludedData.SourceContextAttribute;
                    });
                }
            }
        });

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry distributed tracing and metrics.
    /// Sets up instrumentation for ASP.NET Core, HTTP clients, EF Core, SignalR, Redis, and more.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="isClusterEnvironment">Whether running in a cluster environment.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOpenTelemetryInternal(
        this IServiceCollection services,
        bool isClusterEnvironment,
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
        if (!string.IsNullOrWhiteSpace(oltpExporterEndpoint))
        {
            var otel = services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: OutboxRelayInstrumentation.AppName,
                        serviceVersion: OutboxRelayInstrumentation.Version)
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

                    metrics.SetExemplarFilter(isClusterEnvironment
                        ? ExemplarFilterType.TraceBased
                        : ExemplarFilterType.AlwaysOn);

                    metrics.AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                });
        }

        return services;
    }
}
