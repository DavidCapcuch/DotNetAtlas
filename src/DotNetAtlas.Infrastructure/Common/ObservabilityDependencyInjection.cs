using AspNetCore.SignalR.OpenTelemetry;
using Confluent.Kafka;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Infrastructure.Common.Authentication;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Common.Observability;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Infrastructure.Persistence.Database;
using Elastic.Serilog.Enrichers.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Exceptions.EntityFrameworkCore.Destructurers;
using Serilog.Sinks.OpenTelemetry;
using Serilog.Templates;
using Serilog.Templates.Themes;
using SerilogTracing.Expressions;
using StackExchange.Redis;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for observability infrastructure.
/// Configures logging (Serilog) and distributed tracing/metrics (OpenTelemetry).
/// </summary>
public static class ObservabilityDependencyInjection
{
    /// <summary>
    /// Configures Serilog logging with sinks and enrichers.
    /// Sets up console, Seq, and OpenTelemetry sinks based on environment.
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

            var httpAccessor = services.GetRequiredService<IHttpContextAccessor>();

            configuration
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.WithEcsHttpContext(httpAccessor)
                .Enrich.FromLogContext()
                .Enrich.WithExceptionDetails(new DestructuringOptionsBuilder()
                    .WithDefaultDestructurers()
                    .WithDestructurers([new DbUpdateExceptionDestructurer()]));
            if (isClusterEnvironment)
            {
                configuration.WriteTo.Async(sinkConf => sinkConf.Console());
            }
            else
            {
                configuration.WriteTo.Async(sinkConf =>
                {
                    var loggerConf = sinkConf.Console(new ExpressionTemplate(
                            "[{@t:HH:mm:ss} {@l:u3}] " +
                            "[{Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}] " +
                            "{#if IsRootSpan()}\u2514\u2500 {#else if IsSpan()}\u251c {#else if @sp is not null}\u2502 {#end}" +
                            "{@m}" +
                            "{#if IsSpan()} ({Milliseconds(Elapsed()):0.###} ms){#end}" +
                            "\n" +
                            "{@x}",
                            theme: TemplateTheme.Code,
                            nameResolver: new TracingNameResolver()))
                        .WriteTo.Seq("http://localhost:5341");

                    if (!string.IsNullOrWhiteSpace(oltpExporterEndpoint))
                    {
                        loggerConf.WriteTo.OpenTelemetry(options =>
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
                });
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
    public static IServiceCollection AddOpenTelemetry(
        this IServiceCollection services,
        bool isClusterEnvironment,
        ConfigurationManager configuration)
    {
        services.AddMetrics();

        services.AddSingleton<IDotNetAtlasInstrumentation, DotNetAtlasInstrumentation>();

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
                    tracing.AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = true;
                            options.Filter = context =>
                                !context.Request.Path.StartsWithSegments(
                                    InfrastructureConstants.HealthEndpointPath, StringComparison.OrdinalIgnoreCase)
                                && !context.Request.Path.StartsWithSegments(
                                    InfrastructureConstants
                                        .ReadinessEndpointPath,
                                    StringComparison.OrdinalIgnoreCase);
                        })
                        .AddHttpClientInstrumentation()
                        .AddEntityFrameworkCoreInstrumentation(options => options.SetDbStatementForText = true)
                        .AddSignalRInstrumentation()
                        .AddRedisInstrumentation(options => options.SetVerboseDatabaseStatements = true)
                        .AddFusionCacheInstrumentation()
                        .AddHangfireInstrumentation(options =>
                        {
                            options.RecordException = true;
                        })
                        .AddSource("*");

                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                })
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter("*")
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddFusionCacheInstrumentation()
                        .AddProcessInstrumentation();

                    metrics.SetExemplarFilter(isClusterEnvironment
                        ? ExemplarFilterType.TraceBased
                        : ExemplarFilterType.AlwaysOn);

                    metrics.AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                });
        }

        return services;
    }

    internal static IServiceCollection AddHealthChecksInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        var openMeteoOptions = configuration
            .GetRequiredSection(OpenMeteoOptions.Section)
            .Get<OpenMeteoOptions>()!;
        var weatherApiComOptions = configuration
            .GetRequiredSection(WeatherApiComOptions.Section)
            .Get<WeatherApiComOptions>()!;
        var fusionAuthUrl = configuration[$"{AuthConfigSections.OAuthConfigSection}:Authority"]!;

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BrokersFlat
        };

        services.AddHealthChecks()
            .AddCheck("Self", () => HealthCheckResult.Healthy(),
                tags: [InfrastructureConstants.LivenessTag, InfrastructureConstants.ReadinessTag],
                timeout: TimeSpan.FromSeconds(2))
            .AddDbContextCheck<WeatherContext>(
                name: "Weather DB",
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.DatabaseTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.DatabaseTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: TimeSpan.FromSeconds(4),
                name: "Redis")
            .AddUrlGroup(
                new Uri(weatherApiComOptions.BaseUrl), weatherApiComOptions.BaseUrl,
                tags:
                [
                    InfrastructureConstants.ReadinessTag, InfrastructureConstants.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: TimeSpan.FromSeconds(3))
            .AddUrlGroup(
                new Uri($"{openMeteoOptions.GeoBaseUrl}v1/search?name=Berlin&count=1"), openMeteoOptions.GeoBaseUrl,
                tags:
                [
                    InfrastructureConstants.ReadinessTag, InfrastructureConstants.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: TimeSpan.FromSeconds(3))
            .AddUrlGroup(
                new Uri($"{openMeteoOptions.BaseUrl}v1/forecast"), openMeteoOptions.BaseUrl,
                tags:
                [
                    InfrastructureConstants.ReadinessTag, InfrastructureConstants.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: TimeSpan.FromSeconds(3))
            .AddOpenIdConnectServer(
                oidcSvrUri: new Uri(fusionAuthUrl),
                discoverConfigurationSegment: "/.well-known/openid-configuration",
                name: "FusionAuth IDM",
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.ApiTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: TimeSpan.FromSeconds(3))
            .AddHangfire(options => options.MaximumJobsFailed = 3, "Hangfire Degraded Check",
                failureStatus: HealthStatus.Degraded,
                tags: [InfrastructureConstants.ReadinessTag],
                timeout: TimeSpan.FromSeconds(3))
            .AddHangfire(options =>
                {
                    options.MaximumJobsFailed = 10;
                    options.MinimumAvailableServers = 1;
                }, "Hangfire Unhealthy Check",
                failureStatus: HealthStatus.Unhealthy,
                tags: [InfrastructureConstants.ReadinessTag],
                timeout: TimeSpan.FromSeconds(3))
            .AddKafka(producerConfig, "healthchecks", "Kafka",
                tags: [InfrastructureConstants.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy);

        services.AddHealthChecksUI(settings =>
            {
                settings.SetEvaluationTimeInSeconds(5);
                settings.AddHealthCheckEndpoint("Liveness", InfrastructureConstants.HealthEndpointPath);
                settings.AddHealthCheckEndpoint("Readiness", InfrastructureConstants.ReadinessEndpointPath);
                settings.SetNotifyUnHealthyOneTimeUntilChange();
            })
            .AddSqlServerStorage(configuration.GetConnectionString(nameof(ConnectionStringsOptions.Weather))!);

        return services;
    }

    public static WebApplication UseHealthChecksPrometheusExporterInternal(this WebApplication app)
    {
        // Suppress default prometheus-net collectors and collect only health-related metrics to avoid duplicated scraping.
        // As of now, there is no standardized way to push health metrics through OTEL Collector
        // all other collected metrics are unaffected and still exported through OTEL Collector to prometheus.
        Metrics.SuppressDefaultMetrics();

        app.UseHealthChecksPrometheusExporter(InfrastructureConstants.PrometheusEndpointPath, options =>
        {
            options.Predicate = healthCheck => healthCheck.Tags.Contains(InfrastructureConstants.ReadinessTag);
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
