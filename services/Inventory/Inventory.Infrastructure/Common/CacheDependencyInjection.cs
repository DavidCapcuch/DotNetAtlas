using Inventory.Application.StockItems.Common;
using Inventory.Infrastructure.Common.Config;
using Inventory.Infrastructure.Persistence.Caching;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.CysharpMemoryPack;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// DI wiring for the Inventory-owned read-through stock-availability cache (ADR-0034):
/// binds <see cref="StockLevelCacheOptions"/>, registers a keyed
/// <see cref="IConnectionMultiplexer"/> pointed at <c>redis-cache</c> (ADR-0016 — NEVER
/// <c>redis-basket</c>), a named FusionCache with MemoryPack serialization and the memory
/// level disabled (L2-only: one shared cached copy, evicted on projection update), and
/// binds <see cref="IStockLevelCache"/> to <see cref="FusionStockLevelCache"/>.
/// </summary>
internal static class CacheDependencyInjection
{
    /// <summary>
    /// Config connection-string name for the cache instance: <c>ConnectionStrings:Redis:Cache</c>
    /// (ADR-0016). The colon-bearing path is read as a literal key (cannot bind to a CLR property).
    /// </summary>
    internal const string RedisCacheConnectionStringName = "Redis:Cache";

    internal static IServiceCollection AddStockLevelCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptionsWithValidateOnStart<StockLevelCacheOptions>()
            .BindConfiguration(StockLevelCacheOptions.Section)
            .ValidateDataAnnotations();

        var options = configuration
            .GetRequiredSection(StockLevelCacheOptions.Section)
            .Get<StockLevelCacheOptions>()!;

        var connectionString = configuration.GetConnectionString(RedisCacheConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{RedisCacheConnectionStringName}' is not configured. " +
                $"The Inventory stock-availability read-through cache requires the " +
                $"'{RedisCacheConnectionStringName}' entry (redis-cache per ADR-0016 + ADR-0034).");
        }

        services.AddKeyedSingleton<IConnectionMultiplexer>(
            FusionStockLevelCache.CacheName,
            (_, _) => ConnectionMultiplexer.Connect(connectionString));

        services
            .AddFusionCache(FusionStockLevelCache.CacheName)
            .WithDefaultEntryOptions(entryOptions =>
            {
                // Short TTL = the common-case display-staleness bound (ADR-0034); invalidate-
                // on-projection-update is the primary freshness mechanism. Fail-safe is a
                // SEPARATE window that only serves stale when the projection read itself fails.
                entryOptions.Duration = options.Ttl;
                entryOptions.IsFailSafeEnabled = options.FailSafeEnabled;
                entryOptions.FailSafeMaxDuration = options.FailSafeMaxDuration;

                // L2-only: the redis-cache copy is the single shared truth, so an eviction on
                // the shared key is seen by every instance without a backplane.
                entryOptions.SetSkipMemoryCache(true);

                // Graceful degradation (ADR-0034 + ADR-0016): a volatile-cache problem must
                // degrade the DISPLAY read to the projection, never surface as a 5xx.
                // Distributed-cache exceptions already default to non-rethrow (a down
                // redis-cache → treated as a miss → factory rebuilds); serialization exceptions
                // default to rethrow, so a stale/incompatible MemoryPack payload left in L2
                // across a CachedStockLevel shape change would otherwise throw out of the read
                // path until the TTL elapsed. Force both off so a bad/unreachable L2 entry is
                // treated as a miss and rebuilt from current_stock_levels.
                entryOptions.ReThrowDistributedCacheExceptions = false;
                entryOptions.ReThrowSerializationExceptions = false;
            })
            .WithSerializer(new FusionCacheCysharpMemoryPackSerializer())
            .WithDistributedCache(serviceProvider =>
                new RedisCache(new RedisCacheOptions
                {
                    ConnectionMultiplexerFactory = () =>
                        Task.FromResult(serviceProvider.GetRequiredKeyedService<IConnectionMultiplexer>(
                            FusionStockLevelCache.CacheName)),
                }));

        services.AddSingleton<IStockLevelCache, FusionStockLevelCache>();

        return services;
    }
}
