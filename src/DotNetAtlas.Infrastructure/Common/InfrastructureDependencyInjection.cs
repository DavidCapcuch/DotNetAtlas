using AspNetCore.SignalR.OpenTelemetry;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Common.Observability;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Infrastructure.Persistence.Database.Interceptors;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using Elastic.Serilog.Enrichers.Web;
using EntityFramework.Exceptions.SqlServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Exceptions.EntityFrameworkCore.Destructurers;
using Serilog.Templates;
using Serilog.Templates.Themes;
using SerilogTracing.Expressions;

namespace DotNetAtlas.Infrastructure.Common;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isClusterEnvironment)
    {
        services.AddOptionsWithValidateOnStart<ApplicationOptions>()
            .Bind(configuration.GetSection(ApplicationOptions.Section));

        services.AddObservability(isClusterEnvironment, configuration);
        services.AddDatabase(configuration);

        return services;
    }

    public static WebApplicationBuilder UseSerilogConfiguration(this WebApplicationBuilder builder, bool isClusterEnvironment)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
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
                configuration.WriteTo.Async(sinkConf => sinkConf.Console(new ExpressionTemplate(
                            "[{@t:HH:mm:ss} {@l:u3}] " +
                            "[{Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}] " +
                            "{#if IsRootSpan()}\u2514\u2500 {#else if IsSpan()}\u251c {#else if @sp is not null}\u2502 {#end}" +
                            "{@m}" +
                            "{#if IsSpan()} ({Milliseconds(Elapsed()):0.###} ms){#end}" +
                            "\n" +
                            "{@x}",
                            theme: TemplateTheme.Code,
                            nameResolver: new TracingNameResolver()))
                        .WriteTo.Seq("http://localhost:5341"));
            }
        });

        return builder;
    }

    private static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<UpdateAuditableEntitiesInterceptor>();
        services.AddDbContext<WeatherForecastContext>((
            sp,
            options) => options
            .UseSqlServer(
                configuration.GetConnectionString(ConnectionStrings.Weather),
                sqlServerOptions =>
                {
                    sqlServerOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "weather");
                    sqlServerOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(3),
                        errorNumbersToAdd: null);
                })
            .UseExceptionProcessor()
            .UseSeeding()
            .UseAsyncSeeding()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>()));
        services.AddScoped<IWeatherForecastContext, WeatherForecastContext>();

        return services;
    }

    private static IServiceCollection AddObservability(
        this IServiceCollection services,
        bool isClusterEnvironment,
        IConfiguration configuration)
    {
        services.AddMetrics();

        services.AddSingleton<IDotNetAtlasInstrumentation, DotNetAtlasInstrumentation>();

        var applicationOptions =
            configuration.GetRequiredSection(ApplicationOptions.Section).Get<ApplicationOptions>()!;

        // Be careful of ENV variables overriding what is set in appsettings.json for otel collector
        // OTEL_EXPORTER_OTLP_ENDPOINT is standardized can be set as ENV e.g., by Rider OpenTelemetry plugin
        var oltpExporterEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(oltpExporterEndpoint))
        {
            var otel = services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: applicationOptions.AppName, serviceVersion: ApplicationInfo.Version)
                    .AddContainerDetector()
                    .AddHostDetector())
                .WithTracing(tracing =>
                {
                    tracing.AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = true;
                            options.Filter = context =>
                                !context.Request.Path.StartsWithSegments(
                                    InfrastructureContants.HealthEndpointPath, StringComparison.OrdinalIgnoreCase)
                                && !context.Request.Path.StartsWithSegments(
                                    InfrastructureContants
                                    .ReadinessEndpointPath,
                                    StringComparison.OrdinalIgnoreCase);
                        })
                        .AddHttpClientInstrumentation(options => options.RecordException = true)
                        .AddEntityFrameworkCoreInstrumentation(options => options.SetDbStatementForText = true)
                        .AddSignalRInstrumentation()
                        .AddRedisInstrumentation(options => options.SetVerboseDatabaseStatements = true)
                        .AddSource("*");

                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(oltpExporterEndpoint));
                })
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter("*")
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
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
