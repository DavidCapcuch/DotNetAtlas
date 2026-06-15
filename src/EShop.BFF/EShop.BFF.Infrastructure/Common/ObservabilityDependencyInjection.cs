using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Common.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Pii;
using StackExchange.Redis;

namespace EShop.BFF.Infrastructure.Common;

/// <summary>
/// OpenTelemetry tracing + metrics for the BFF. Instruments ASP.NET Core (inbound), the typed
/// HttpClients (outbound — these propagate <c>traceparent</c> so traces span Client → BFF → upstream,
/// bff.md § 2.4), the <c>redis-cache</c> multiplexer, and FusionCache.
/// </summary>
internal static class ObservabilityDependencyInjection
{
    public static IServiceCollection AddBffObservability(
        this IServiceCollection services,
        bool isDeployedEnvironment,
        IConfiguration configuration)
    {
        services.AddMetrics();

        var otlpExporterEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(otlpExporterEndpoint))
        {
            return services;
        }

        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? ApplicationInfo.AppName;

        services.AddOpenTelemetry()
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
                    .AddRedisInstrumentation(options => options.SetVerboseDatabaseStatements = true)
                    // The redis-cache multiplexer is registered as a KEYED singleton, so the unkeyed
                    // AddRedisInstrumentation() above never discovers it — add it explicitly (ADR-0016).
                    .ConfigureRedisInstrumentation((sp, instrumentation) =>
                        instrumentation.AddConnection(
                            sp.GetRequiredKeyedService<IConnectionMultiplexer>(BffCacheConstants.CacheName)))
                    .AddFusionCacheInstrumentation()
                    .AddSource("*")
                    .AddPiiRedactionProcessor(); // ADR-0011 — redacts [Pii]-tagged span attributes before export

                tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpExporterEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter("*")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddFusionCacheInstrumentation()
                    .AddProcessInstrumentation();

                metrics.SetExemplarFilter(isDeployedEnvironment
                    ? ExemplarFilterType.TraceBased
                    : ExemplarFilterType.AlwaysOn);

                metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpExporterEndpoint));
            });

        return services;
    }
}
