using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AspNetCore.SignalR.OpenTelemetry;
using Confluent.Kafka;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.Forecast.Common.Abstractions;
using DotNetAtlas.Application.Forecast.Common.Config;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Infrastructure.Common.Authentication;
using DotNetAtlas.Infrastructure.Common.Authorization;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Common.Observability;
using DotNetAtlas.Infrastructure.Communication.Kafka;
using DotNetAtlas.Infrastructure.Communication.Kafka.Config;
using DotNetAtlas.Infrastructure.HttpClients.Weather.OpenMeteo;
using DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiCom;
using DotNetAtlas.Infrastructure.Jobs;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Infrastructure.Persistence.Database.Interceptors;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using DotNetAtlas.Infrastructure.SignalR;
using Elastic.Serilog.Enrichers.Web;
using EntityFramework.Exceptions.SqlServer;
using Hangfire;
using Hangfire.SqlServer;
using HealthChecks.UI.Client;
using KafkaFlow;
using KafkaFlow.Configuration;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
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
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.CysharpMemoryPack;

namespace DotNetAtlas.Infrastructure.Common;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isClusterEnvironment)
    {
        services.AddOptionsWithValidateOnStart<ApplicationOptions>()
            .Bind(configuration.GetSection(ApplicationOptions.Section))
            .ValidateDataAnnotations();

        services.AddObservability(isClusterEnvironment, configuration);
        services.AddOptionsWithValidateOnStart<WeatherHedgingOptions>()
            .Bind(configuration.GetSection(WeatherHedgingOptions.Section))
            .ValidateDataAnnotations();

        services.AddAuthenticationInternal(configuration, isClusterEnvironment);
        services.AddAuthorizationInternal();

        services.AddDatabase(configuration, isClusterEnvironment);
        services.AddCache(configuration);

        services.AddSignalRInfrastructure(configuration);
        services.AddWeatherApiClients(configuration);
        services.AddHealthChecksInternal(configuration);
        services.AddBackgroundJobs(configuration);
        services.AddKafkaMessaging(configuration);

        return services;
    }

    private static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<KafkaOptions>()
            .Bind(configuration.GetSection(KafkaOptions.Section))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<TopicsOptions>()
            .Bind(configuration.GetSection(TopicsOptions.Section))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<KafkaForecastEventsProducerOptions>()
            .Bind(configuration.GetSection(KafkaForecastEventsProducerOptions.Section))
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var producerOptions = configuration
            .GetRequiredSection(KafkaForecastEventsProducerOptions.Section)
            .Get<KafkaForecastEventsProducerOptions>()!;

        services.AddKafka(kafka => kafka
            .AddCluster(cluster => cluster
                .WithBrokers(kafkaOptions.Brokers)
                .WithSchemaRegistry(config => config.Url = kafkaOptions.SchemaRegistry.Url)
                .AddProducer<KafkaForecastEventsProducer>(producer =>
                    producer
                        .WithProducerConfig(producerOptions)
                        .AddMiddlewares(m =>
                            m.AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer))
                ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddSingleton<IForecastEventsProducer, KafkaForecastEventsProducer>();

        return services;
    }

    private static IServiceCollection AddWeatherApiClients(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<OpenMeteoOptions>()
            .Bind(configuration.GetSection(OpenMeteoOptions.Section))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<WeatherApiComOptions>()
            .Bind(configuration.GetSection(WeatherApiComOptions.Section))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HttpResilienceOptions>()
            .Bind(configuration.GetSection(HttpResilienceOptions.Section))
            .ValidateDataAnnotations();

        var httpResilienceOptions = configuration
            .GetRequiredSection(HttpResilienceOptions.Section)
            .Get<HttpResilienceOptions>()!;

        services.AddHttpClient(OpenMeteoWeatherProvider.HttpClientName, (sp, config) =>
            {
                var openMeteoOptions =
                    sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
                config.BaseAddress = new Uri(openMeteoOptions.BaseUrl);
            })
            .AddAsKeyed()
            .AddDefaultResilienceHandler(httpResilienceOptions);

        services.AddHttpClient(OpenMeteoGeocodingService.GeoHttpClientName, (sp, config) =>
            {
                var openMeteoOptions =
                    sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
                config.BaseAddress = new Uri(openMeteoOptions.GeoBaseUrl);
            })
            .AddAsKeyed()
            .AddDefaultResilienceHandler(httpResilienceOptions);

        services.AddHttpClient(WeatherApiComProvider.HttpClientName, (sp, config) =>
            {
                var weatherApiComOptions =
                    sp.GetRequiredService<IOptions<WeatherApiComOptions>>().Value;
                config.BaseAddress = new Uri(weatherApiComOptions.BaseUrl);
            })
            .AddAsKeyed()
            .AddDefaultResilienceHandler(httpResilienceOptions);

        services.AddKeyedScoped<IGeocodingService, OpenMeteoGeocodingService>(OpenMeteoGeocodingService.ServiceKey);
        services
            .AddKeyedScoped<IGeocodingService, WeatherApiComGeocodingService>(WeatherApiComGeocodingService.ServiceKey);
        services.AddScoped<IGeocodingService, OpenMeteoGeocodingService>();
        services.AddScoped<IMainWeatherForecastProvider, OpenMeteoWeatherProvider>();
        services.AddScoped<IWeatherForecastProvider, OpenMeteoWeatherProvider>();
        services.AddScoped<IWeatherForecastProvider, WeatherApiComProvider>();

        return services;
    }

    /// <summary>
    /// Cannot use ConfigureHttpClientDefaults because it is applied to health check clients too
    /// which then fail if a degraded service is encountered.
    /// </summary>
    private static IHttpResiliencePipelineBuilder AddDefaultResilienceHandler(
        this IHttpClientBuilder builder,
        HttpResilienceOptions httpResilienceOptions)
    {
        return builder.AddResilienceHandler(
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
            });
    }

    public static WebApplicationBuilder UseSerilogInternal(
        this WebApplicationBuilder builder,
        bool isClusterEnvironment)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var oltpExporterEndpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]!;
            var appName = context.Configuration[$"{ApplicationOptions.Section}:AppName"]!;

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
                                ["service.name"] = appName
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

    private static IServiceCollection AddCache(this IServiceCollection services, ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<DefaultCacheOptions>()
            .Bind(configuration.GetSection(DefaultCacheOptions.Section))
            .ValidateDataAnnotations();

        var defaultCacheOptions =
            configuration.GetRequiredSection(DefaultCacheOptions.Section)
                .Get<DefaultCacheOptions>()!;

        // App cache
        services.AddFusionCache()
            .WithOptions(options =>
            {
                options.DistributedCacheCircuitBreakerDuration =
                    TimeSpan.FromSeconds(defaultCacheOptions.DistributedCacheCircuitBreakerSeconds);
                options.IncludeTagsInLogs = defaultCacheOptions.IncludeTagsInLogs;
                options.IncludeTagsInTraces = defaultCacheOptions.IncludeTagsInTraces;
                options.IncludeTagsInMetrics = defaultCacheOptions.IncludeTagsInMetrics;
            })
            .WithDefaultEntryOptions(options =>
            {
                options.Duration = TimeSpan.FromMinutes(defaultCacheOptions.DefaultDurationMinutes);

                options.FactorySoftTimeout = TimeSpan.FromMilliseconds(defaultCacheOptions.FactorySoftTimeoutMs);
                options.FactoryHardTimeout = TimeSpan.FromMilliseconds(defaultCacheOptions.FactoryHardTimeoutMs);

                options.DistributedCacheSoftTimeout =
                    TimeSpan.FromSeconds(defaultCacheOptions.DistributedCacheSoftTimeoutSeconds);
                options.DistributedCacheHardTimeout =
                    TimeSpan.FromSeconds(defaultCacheOptions.DistributedCacheHardTimeoutSeconds);

                options.AllowBackgroundDistributedCacheOperations =
                    defaultCacheOptions.AllowBackgroundDistributedCacheOperations;
                options.AllowBackgroundBackplaneOperations = defaultCacheOptions.AllowBackgroundBackplaneOperations;
                options.JitterMaxDuration = TimeSpan.FromSeconds(defaultCacheOptions.JitterMaxDurationSeconds);
            })
            .WithSerializer(
                new FusionCacheCysharpMemoryPackSerializer()
            )
            // Use the centralized multiplexer for the distributed cache
            .WithDistributedCache(sp =>
                new RedisCache(new RedisCacheOptions
                {
                    ConnectionMultiplexerFactory =
                        () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>())
                })
            )
            // Use the centralized multiplexer for the backplane
            .WithBackplane(sp =>
                new RedisBackplane(new RedisBackplaneOptions
                {
                    ConnectionMultiplexerFactory =
                        () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>())
                })
            );

        // Api output cache (for openapi, generated clients etc.)
        services.AddFusionOutputCache();
        services.AddOutputCache();

        return services;
    }

    private static IServiceCollection AddDatabase(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isClusterEnvironment)
    {
        services.AddOptionsWithValidateOnStart<EfCoreOptions>()
            .Bind(configuration.GetSection(EfCoreOptions.Section))
            .ValidateDataAnnotations();

        var efCoreOptions = configuration
            .GetRequiredSection(EfCoreOptions.Section)
            .Get<EfCoreOptions>()!;

        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();
        services.AddDbContextPool<WeatherForecastContext>((
            sp,
            options) => options
            .UseSqlServer(
                configuration.GetConnectionString(ConnectionStrings.Weather),
                sqlServerOptions =>
                {
                    sqlServerOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "weather");
                    sqlServerOptions.UseQuerySplittingBehavior(
                        efCoreOptions.UseQuerySplitting
                            ? QuerySplittingBehavior.SplitQuery
                            : QuerySplittingBehavior.SingleQuery);
                    sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: efCoreOptions.RetryMaxCount,
                        maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                        errorNumbersToAdd: null);
                })
            .EnableSensitiveDataLogging(!isClusterEnvironment)
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors)
            .UseExceptionProcessor()
            .UseSeeding()
            .UseAsyncSeeding()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>()),
            poolSize: efCoreOptions.DbContextPoolSize);
        services.AddScoped<IWeatherForecastContext, WeatherForecastContext>();

        return services;
    }

    private static IServiceCollection AddObservability(
        this IServiceCollection services,
        bool isClusterEnvironment,
        ConfigurationManager configuration)
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

    private static IServiceCollection AddAuthenticationInternal(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isClusterEnvironment)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = AuthPolicySchemes.JwtOrCookie;
                options.DefaultAuthenticateScheme = AuthPolicySchemes.JwtOrCookie;
                options.DefaultChallengeScheme = AuthPolicySchemes.JwtOrCookie;
            })
            .AddPolicyScheme(AuthPolicySchemes.JwtOrCookie, AuthPolicySchemes.JwtOrCookie, options =>
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    var path = ctx.Request.Path;
                    var hasAuthHeader = ctx.Request.Headers.ContainsKey("Authorization");
                    if (hasAuthHeader ||
                        path.StartsWithSegments(InfrastructureConstants.ApiBasePath,
                            StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWithSegments(InfrastructureConstants.HubsBasePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return JwtBearerDefaults.AuthenticationScheme;
                    }

                    return CookieAuthenticationDefaults.AuthenticationScheme;
                };
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                configuration.Bind(AuthConfigSections.JwtBearerConfigSection, options);
                if (isClusterEnvironment)
                {
                    options.RequireHttpsMetadata = true;
                }

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // For SignalR auth. Sending the access token in the query string is required
                        // when using WebSockets or ServerSentEvents due to a limitation in Browser APIs.
                        // See https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-9.0
                        if (context.HttpContext.Request.Path.StartsWithSegments(InfrastructureConstants.HubsBasePath,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            var accessToken = context.Request.Query["access_token"];
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                configuration.Bind(AuthConfigSections.CookieConfigSection, options);
                options.Cookie.IsEssential = true;
                options.Cookie.HttpOnly = true;

                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments(InfrastructureConstants.ApiBasePath,
                                StringComparison.OrdinalIgnoreCase) ||
                            ctx.Request.Path.StartsWithSegments(InfrastructureConstants.HubsBasePath,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    }
                };
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                configuration.Bind(AuthConfigSections.OidcConfigSection, options);
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                foreach (var scope in AuthScopes.List)
                {
                    options.Scope.Add(scope.Name);
                }

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!string.IsNullOrWhiteSpace(context.TokenEndpointResponse?.AccessToken))
                        {
                            var handler = new JwtSecurityTokenHandler();
                            var jwtAccessToken = handler.ReadJwtToken(context.TokenEndpointResponse.AccessToken);
                            if (jwtAccessToken == null)
                            {
                                return Task.CompletedTask;
                            }

                            var roles = jwtAccessToken.Claims
                                .Where(c => c.Type is "roles" or "role")
                                .Select(c => c.Value)
                                .ToList();
                            var roleClaims = roles.Select(r => new Claim(ClaimTypes.Role, r));

                            var identity = new ClaimsIdentity(roleClaims);
                            context.Principal?.AddIdentity(identity);

                            var expiration = jwtAccessToken.ValidTo;
                            if (context.Properties is not null)
                            {
                                context.Properties.ExpiresUtc = expiration;
                                context.Properties.IsPersistent = true;
                            }
                        }

                        return Task.CompletedTask;
                    }
                };

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

    private static IServiceCollection AddSignalRInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        IConnectionMultiplexer redisMultiplexer =
            ConnectionMultiplexer.Connect(configuration.GetConnectionString(ConnectionStrings.Redis)!);
        services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
        services.AddSingleton<IGroupManager, RedisSignalRGroupManager>();

        services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(120);
            })
            .AddJsonProtocol()
            .AddMessagePackProtocol(options =>
            {
                options.SerializerOptions = MessagePackSerializerOptions.Standard
                    .WithResolver(ContractlessStandardResolver.Instance)
                    .WithSecurity(MessagePackSecurity.UntrustedData);
            })
            .AddHubInstrumentation()
            .AddStackExchangeRedis(options =>
            {
                options.ConnectionFactory = _ => Task.FromResult(redisMultiplexer);
                options.Configuration.ChannelPrefix = RedisChannel.Literal("signalr.dotnetatlas");
            });

        return services;
    }

    private static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<FakeWeatherAlertJobOptions>()
            .Bind(configuration.GetSection(FakeWeatherAlertJobOptions.Section))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HangfireOptions>()
            .Bind(configuration.GetSection(HangfireOptions.Section))
            .ValidateDataAnnotations();

        var hangfireOptions = configuration
            .GetRequiredSection(HangfireOptions.Section)
            .Get<HangfireOptions>()!;

        services.AddHangfire(config =>
        {
            config.UseRecommendedSerializerSettings();
            config.UseSimpleAssemblyNameTypeSerializer();
            config.UseSerilogLogProvider();
            config.UseSqlServerStorage(configuration.GetConnectionString(ConnectionStrings.Weather),
                new SqlServerStorageOptions()
                {
                    JobExpirationCheckInterval =
                        TimeSpan.FromMilliseconds(hangfireOptions.JobExpirationCheckIntervalMs),
                    QueuePollInterval = TimeSpan.FromMilliseconds(hangfireOptions.QueuePollIntervalMs)
                });
        });

        services.AddHangfireServer(options =>
        {
            options.SchedulePollingInterval = TimeSpan.FromMilliseconds(hangfireOptions.SchedulePollingIntervalMs);
            options.CancellationCheckInterval = TimeSpan.FromMilliseconds(hangfireOptions.CancellationCheckIntervalMs);
            options.Queues = hangfireOptions.Queues;
        });

        services.AddScoped<IWeatherAlertJobScheduler, HangfireWeatherAlertJobScheduler>();

        return services;
    }

    private static IServiceCollection AddHealthChecksInternal(
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
            .AddDbContextCheck<WeatherForecastContext>(
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
            .AddSqlServerStorage(configuration.GetConnectionString(ConnectionStrings.Weather)!);

        return services;
    }

    public static WebApplication MapHealthChecksInternal(this WebApplication app)
    {
        app.MapHealthChecks(InfrastructureConstants.ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(InfrastructureConstants.ReadinessTag),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).ShortCircuit();

        app.MapHealthChecks(InfrastructureConstants.HealthEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(InfrastructureConstants.LivenessTag),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).ShortCircuit();

        app.MapHealthChecksUI()
            .RequireAuthorization(AuthPolicies.DevOnly);

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
