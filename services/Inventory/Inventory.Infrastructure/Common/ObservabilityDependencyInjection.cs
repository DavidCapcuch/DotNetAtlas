using Inventory.Infrastructure.Common.Observability;
using Inventory.Infrastructure.Persistence.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Pii;
using StackExchange.Redis;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for observability infrastructure.
/// Configures distributed tracing/metrics (OpenTelemetry) for the Inventory BC.
/// </summary>
public static class ObservabilityDependencyInjection
{
    /// <summary>
    /// Configures OpenTelemetry distributed tracing and metrics.
    /// Sets up instrumentation for ASP.NET Core, HTTP clients, EF Core, and Redis
    /// (FastEndpoints idempotency cache per ADR-0013). Gated on
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> so unconfigured hosts skip OTel entirely.
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
                        .AddRedisInstrumentation(options => options.SetVerboseDatabaseStatements = true)
                        // ADR-0034 + ADR-0016: the read-through stock-availability cache registers its
                        // redis-cache multiplexer as a KEYED singleton (CacheDependencyInjection). The
                        // AddRedisInstrumentation() above only discovers the unkeyed IConnectionMultiplexer,
                        // so the keyed instance is added to the instrumentation explicitly — otherwise the
                        // cache's redis-cache hops never surface as spans. (FastEndpoints' idempotency output
                        // cache owns a separate internal multiplexer, unaffected by either call.)
                        .ConfigureRedisInstrumentation((sp, instrumentation) =>
                            instrumentation.AddConnection(
                                sp.GetRequiredKeyedService<IConnectionMultiplexer>(FusionStockLevelCache.CacheName)))
                        .AddSource("*")
                        .AddPiiRedactionProcessor(); // ADR-0011 — redacts [Pii]-tagged span attributes before export

                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                })
                .WithMetrics(metrics =>
                {
                    // AddMeter("*") catches InventoryMetrics (ADR-0006 § Observability) without
                    // having to enumerate per-Meter names; the BC's only Meter today is "Inventory".
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
