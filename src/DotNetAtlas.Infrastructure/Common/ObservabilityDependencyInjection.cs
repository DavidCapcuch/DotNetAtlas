using AspNetCore.SignalR.OpenTelemetry;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Common.Observability;
using Elastic.Serilog.Enrichers.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Exceptions.EntityFrameworkCore.Destructurers;
using Serilog.Sinks.OpenTelemetry;
using Serilog.Templates;
using Serilog.Templates.Themes;
using SerilogTracing.Expressions;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for observability infrastructure.
/// Configures logging (Serilog) and distributed tracing/metrics (OpenTelemetry).
/// </summary>
public static class ObservabilityDependencyInjection
{
    /// <summary>
    /// Configures Serilog logging with sinks and enrichers.
    /// Sets up console, Seq, and OpenTelemetry sinks based on the environment.
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
                configuration.WriteTo.Console();
            }
            else
            {
                configuration.WriteTo.Console(new ExpressionTemplate(
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
                                    InfrastructureConstants.ReadinessEndpointPath, StringComparison.OrdinalIgnoreCase);
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
}
