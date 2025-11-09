using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Domain.Common;
using DotNetAtlas.Domain.Entities.Weather.Feedback.Events;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.DomainToAvroMappings;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Infrastructure.Persistence.Database.Interceptors;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using DotNetAtlas.Outbox.EntityFrameworkCore.Common;
using DotNetAtlas.Outbox.EntityFrameworkCore.Core;
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
    /// Sets up connection pooling, interceptors, retry policies, seeding, and outbox pattern.
    /// </summary>
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
        services.AddDbContextPool<WeatherDbContext>((
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
                .UseOutboxEventsInterceptor(sp)
                .AddInterceptors(
                    sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>()),
            poolSize: efCoreOptions.DbContextPoolSize);

        services.AddScoped<IWeatherDbContext, WeatherDbContext>();

        services.AddOutbox<WeatherDbContext>(outbox =>
        {
            outbox.ConfigureAvroSerializerConfig(options =>
            {
                configuration.Bind(AvroSerializerOptions.Section, options);
            });
            outbox.ConfigureSchemaRegistryConfig(options =>
            {
                configuration.Bind(SchemaRegistryOptions.Section, options);
            });

            outbox.RegisterOutboxMessagesBatchExtractionFor<AggregateRoot<Guid>>(agg =>
                new OutboxMessagesBatch(agg.Id.ToString(), agg.PopDomainEvents())
            );

            outbox.RegisterAvroMapperFor<FeedbackChangedDomainEvent>(domainEvent =>
                domainEvent.ToFeedbackChangedEvent());
            outbox.RegisterAvroMapperFor<FeedbackCreatedDomainEvent>(domainEvent =>
                domainEvent.ToFeedbackCreatedEvent());
        });

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
