using AspNetCore.SignalR.OpenTelemetry;
using DotNetAtlas.Application.Common.Config;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Infrastructure.Common.Authentication;
using DotNetAtlas.Infrastructure.Common.Authorization;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Common.Observability;
using DotNetAtlas.Infrastructure.HttpClients.Weather.OpenMeteoProvider;
using DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiComProvider;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Infrastructure.Persistence.Database.Interceptors;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using Elastic.Serilog.Enrichers.Web;
using EntityFramework.Exceptions.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
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
        services.AddOptionsWithValidateOnStart<WeatherHedgingOptions>()
            .Bind(configuration.GetSection(WeatherHedgingOptions.Section));
        services.AddAuthenticationInternal(configuration, isClusterEnvironment);
        services.AddAuthorizationInternal();
        services.AddDatabase(configuration);
        services.AddWeatherApiClients(configuration);

        return services;
    }

    private static IServiceCollection AddWeatherApiClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptionsWithValidateOnStart<OpenMeteoOptions>()
            .Bind(configuration.GetSection(OpenMeteoOptions.Section));
        services.AddOptionsWithValidateOnStart<WeatherApiComOptions>()
            .Bind(configuration.GetSection(WeatherApiComOptions.Section));
        services.AddOptionsWithValidateOnStart<HttpResilienceOptions>()
            .Bind(configuration.GetSection(HttpResilienceOptions.Section));

        var httpResilienceOptions = configuration
            .GetRequiredSection(HttpResilienceOptions.Section)
            .Get<HttpResilienceOptions>()!;

        services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddResilienceHandler(
            "DefaultResiliencePipeline",
            resilienceBuilder =>
            {
                resilienceBuilder
                    .AddTimeout(TimeSpan.FromSeconds(httpResilienceOptions.TotalRequestTimeoutSeconds))
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = httpResilienceOptions.RetryMaxAttempts,
                        UseJitter = true,
                        BackoffType = DelayBackoffType.Exponential,
                        Name = "DefaultRetryPolicy",
                        ShouldHandle = args =>
                            new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                    })
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        SamplingDuration = TimeSpan.FromSeconds(httpResilienceOptions.CircuitBreakerSamplingSeconds),
                        FailureRatio = httpResilienceOptions.CircuitBreakerFailureRatio,
                        MinimumThroughput = httpResilienceOptions.CircuitBreakerMinimumThroughput,
                        BreakDuration = TimeSpan.FromSeconds(httpResilienceOptions.CircuitBreakerBreakSeconds),
                        Name = "DefaultCircuitBreakerPolicy",
                        ShouldHandle = args =>
                            new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                    })
                    .AddTimeout(TimeSpan.FromSeconds(httpResilienceOptions.AttemptTimeoutSeconds));
            }));

        services.AddHttpClient(OpenMeteoWeatherProvider.HttpClientName, (sp, config) =>
            {
                var openMeteoOptions =
                    sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
                config.BaseAddress = new Uri(openMeteoOptions.BaseUrl);
            })
            .AddAsKeyed();

        services.AddHttpClient(OpenMeteoGeocodingService.GeoHttpClientName, (sp, config) =>
            {
                var openMeteoOptions =
                    sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
                config.BaseAddress = new Uri(openMeteoOptions.GeoBaseUrl);
            })
            .AddAsKeyed();

        services.AddHttpClient(WeatherApiComProvider.HttpClientName, (sp, config) =>
            {
                var weatherApiComOptions =
                    sp.GetRequiredService<IOptions<WeatherApiComOptions>>().Value;
                config.BaseAddress = new Uri(weatherApiComOptions.BaseUrl);
            })
            .AddAsKeyed();

        services.AddKeyedScoped<IGeocodingService, OpenMeteoGeocodingService>(OpenMeteoGeocodingService.ServiceKey);
        services.AddKeyedScoped<IGeocodingService, WeatherApiComGeocodingService>(WeatherApiComGeocodingService.ServiceKey);
        services.AddScoped<IMainWeatherForecastProvider, OpenMeteoWeatherProvider>();
        services.AddScoped<IWeatherForecastProvider, OpenMeteoWeatherProvider>();
        services.AddScoped<IWeatherForecastProvider, WeatherApiComProvider>();

        return services;
    }

    public static WebApplicationBuilder UseSerilogConfiguration(
        this WebApplicationBuilder builder,
        bool isClusterEnvironment)
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
                        .AddHttpClientInstrumentation()
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

    private static IServiceCollection AddAuthenticationInternal(this IServiceCollection services,
        IConfiguration configuration,
        bool isClusterEnvironment)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                configuration.Bind(AuthConfigSections.Full.JwtBearer, options);
                if (isClusterEnvironment)
                {
                    options.RequireHttpsMetadata = true;
                }
            })
            .AddOpenIdConnect(SecuritySchemes.Oidc, options =>
            {
                configuration.Bind(AuthConfigSections.Full.OAuthConfig, options);
                foreach (var scope in Scopes.List)
                {
                    options.Scope.Add(scope.Name);
                }

                if (isClusterEnvironment)
                {
                    options.RequireHttpsMetadata = true;
                }
            });
        services.AddHttpContextAccessor();

        return services;
    }

    private static IServiceCollection AddAuthorizationInternal(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.DevOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(Roles.Developer);
            });

        return services;
    }
}
