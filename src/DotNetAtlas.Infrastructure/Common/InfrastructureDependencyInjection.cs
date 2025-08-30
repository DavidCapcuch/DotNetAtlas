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

namespace DotNetAtlas.Infrastructure.Common
{
    public static class InfrastructureDependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration,
            bool isClusterEnvironment)
        {
            services.AddOptionsWithValidateOnStart<ApplicationOptions>()
                .Bind(configuration.GetSection(ApplicationOptions.SECTION));

            services.AddDatabase(configuration);

            return services;
        }

        private static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<UpdateAuditableEntitiesInterceptor>();
            services.AddDbContext<WeatherForecastContext>((
                sp,
                options) => options
                .UseSqlServer(
                    configuration.GetConnectionString(ConnectionStrings.WEATHER),
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
            string appName)
        {
            services.AddMetrics();

            services.AddSingleton<IDotNetAtlasInstrumentation, DotNetAtlasInstrumentation>();

            var otel = services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: appName, serviceVersion: ApplicationInfo.Version)
                    .AddContainerDetector()
                    .AddHostDetector())
                .WithTracing(tracing =>
                {
                    tracing.AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = true;
                            options.Filter = context =>
                                !context.Request.Path.StartsWithSegments(InfrastructureContants.HEALTH_ENDPOINT_PATH)
                                && !context.Request.Path.StartsWithSegments(InfrastructureContants
                                    .READINESS_ENDPOINT_PATH);
                        })
                        .AddHttpClientInstrumentation(options => { options.RecordException = true; })
                        .AddEntityFrameworkCoreInstrumentation(options => { options.SetDbStatementForText = true; })
                        .AddSignalRInstrumentation()
                        .AddRedisInstrumentation(options => options.SetVerboseDatabaseStatements = true)
                        .AddSource("*");

                    tracing.AddOtlpExporter();
                })
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter("*")
                        // .AddSqlClientInstrumentation()
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddProcessInstrumentation();

                    metrics.SetExemplarFilter(isClusterEnvironment
                        ? ExemplarFilterType.TraceBased
                        : ExemplarFilterType.AlwaysOn);

                    metrics.AddOtlpExporter();
                });

            return services;
        }

        public static void UsePlatformSerilog(this WebApplicationBuilder builder, bool isClusterEnvironment)
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
                    configuration.WriteTo.Async(sinkConf => { sinkConf.Console(); });
                }
                else
                {
                    configuration.WriteTo.Async(sinkConf =>
                    {
                        sinkConf.Console(new ExpressionTemplate(
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
                        // .WriteTo.Elasticsearch(
                        //     new ElasticsearchSinkOptions(new Uri("http://localhost:9200"))
                        //     {
                        //         IndexFormat =
                        //             $"{context.HostingEnvironment.ApplicationName.ToLowerInvariant().Replace(".", "-")}" +
                        //             $"-{context.HostingEnvironment.EnvironmentName.ToLowerInvariant().Replace(".", "-")}" +
                        //             $"-{DateTime.UtcNow:yyyy-MM}",
                        //         AutoRegisterTemplate = true,
                        //         NumberOfShards = 2,
                        //         NumberOfReplicas = 1
                        //     });
                    });
                }
            });
        }
    }
}