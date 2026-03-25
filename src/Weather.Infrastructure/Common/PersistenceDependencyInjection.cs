using EntityFramework.Exceptions.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Weather.Application.Common.Data;
using Weather.Infrastructure.Common.Config;
using Weather.Infrastructure.Persistence.Database;
using Weather.Infrastructure.Persistence.Database.Interceptors;
using Weather.Infrastructure.Persistence.Database.Seed;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.CysharpMemoryPack;

namespace Weather.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for persistence infrastructure.
/// Configures database (EF Core) and distributed caching (Redis + FusionCache).
/// </summary>
public static class PersistenceDependencyInjection
{
    /// <summary>
    /// Configures Entity Framework Core database context with SQL Server.
    /// Sets up connection pooling, interceptors, retry policies, seeding, and outbox pattern.
    /// </summary>
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
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

        // See: https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#registering-interceptors
        // DispatchDomainEventsInterceptor is registered as Scoped because it depends on scoped IDomainEventDispatcher,
        // which resolves Domain Event handlers from the same scope as the DbContext.
        // This ensures that the same DbContext is used for both SaveChanges and Domain Event Dispatching
        // and the same DbContext is used in the Domain Event Handlers (e.g., for Outbox dispatching within the same transaction)
        services.AddScoped<DispatchDomainEventsInterceptor>();

        // UpdateAuditableEntitiesInterceptor is registered as Singleton for performance optimization.
        // This is safe because the interceptor is stateless - it doesn't capture or store any per-request data
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();
        services.AddDbContext<WeatherDbContext>((
            sp,
            options) => options
            .UseSqlServer(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Weather)),
                sqlServerOptions =>
                {
                    sqlServerOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                        WeatherDbContext.DefaultSchemaName);
                    sqlServerOptions.UseQuerySplittingBehavior(
                        efCoreOptions.UseQuerySplitting
                            ? QuerySplittingBehavior.SplitQuery
                            : QuerySplittingBehavior.SingleQuery);
                    sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: efCoreOptions.RetryMaxCount,
                        maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                        errorNumbersToAdd: null);
                })
            .EnableSensitiveDataLogging(
                !isDeployedEnvironment) // this is very useful for local debugging/investigating failed tests
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors)
            .UseExceptionProcessor() // required for the Inbox pattern, see Platform.ReliableMessaging.Inbox.EFCore
            .UseSeeding() // see https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding
            .UseAsyncSeeding()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IWeatherDbContext>(sp => sp.GetRequiredService<WeatherDbContext>());

        return services;
    }

    /// <summary>
    /// Configures distributed caching with Redis and FusionCache.
    /// Sets up memory cache, distributed cache, backplane, and output cache.
    /// </summary>
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
            .WithDistributedCache(sp =>
                new RedisCache(new RedisCacheOptions
                {
                    ConnectionMultiplexerFactory =
                        () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>())
                })
            )
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
