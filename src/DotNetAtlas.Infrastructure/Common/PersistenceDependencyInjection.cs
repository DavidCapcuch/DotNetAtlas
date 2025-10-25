using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Infrastructure.Persistence.Database.Interceptors;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using EntityFramework.Exceptions.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.CysharpMemoryPack;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for persistence infrastructure.
/// Configures database (EF Core) and distributed caching (Redis + FusionCache).
/// </summary>
public static class PersistenceDependencyInjection
{
    /// <summary>
    /// Configures Entity Framework Core database context with SQL Server.
    /// Sets up connection pooling, interceptors, retry policies, and seeding.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <param name="isClusterEnvironment">Whether running in a cluster environment.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isClusterEnvironment)
    {
        services.AddOptionsWithValidateOnStart<EfCoreOptions>()
            .BindConfiguration(EfCoreOptions.Section)
            .ValidateDataAnnotations();

        var efCoreOptions = configuration
            .GetRequiredSection(EfCoreOptions.Section)
            .Get<EfCoreOptions>()!;

        services
            .AddOptionsWithValidateOnStart<ConnectionStringsOptions>()
            .BindConfiguration(ConnectionStringsOptions.Section)
            .ValidateDataAnnotations();

        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();
        services.AddDbContextPool<WeatherContext>((
                sp,
                options) => options
                .UseSqlServer(
                    configuration.GetConnectionString(nameof(ConnectionStringsOptions.Weather)),
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
        services.AddScoped<IWeatherContext, WeatherContext>();

        return services;
    }

    /// <summary>
    /// Configures distributed caching with Redis and FusionCache.
    /// Sets up memory cache, distributed cache, backplane, and output cache.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCache(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<DefaultCacheOptions>()
            .BindConfiguration(DefaultCacheOptions.Section)
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
}
