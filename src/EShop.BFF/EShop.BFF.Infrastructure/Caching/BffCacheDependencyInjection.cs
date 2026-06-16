using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace EShop.BFF.Infrastructure.Caching;

/// <summary>
/// DI wiring for the BFF composed-response cache: a keyed <see cref="IConnectionMultiplexer"/> pointed
/// at <c>redis-cache</c> (ADR-0016 — NEVER <c>redis-basket</c>), and FusionCache with a Redis L2
/// distributed cache <em>and</em> a Redis backplane on that same instance so tag invalidations
/// propagate across BFF replicas (bff.md § 3.1.1).
/// </summary>
internal static class BffCacheDependencyInjection
{
    // Product-page defaults (bff.md § 3.1.1). The BFF's only cached entry today, so these are the
    // FusionCache default entry options; later endpoints override per-entry.
    private static readonly TimeSpan ProductPageTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailSafeMaxDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan JitterMaxDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The oldest a still-fresh product-page entry can be (soft TTL + jitter). A page older than this was
    /// served from fail-safe, so the endpoint flags it stale (bff.md § 3.1); see <c>StaleServePolicy</c>.
    /// </summary>
    public static readonly TimeSpan StaleServeFreshWindow = ProductPageTtl + JitterMaxDuration;

    public static IServiceCollection AddBffCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(BffCacheConstants.RedisCacheConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{BffCacheConstants.RedisCacheConnectionStringName}' is not configured. " +
                $"The BFF composed-response cache requires the '{BffCacheConstants.RedisCacheConnectionStringName}' " +
                "entry (redis-cache per ADR-0016) — it MUST NOT point at redis-basket.");
        }

        services.AddKeyedSingleton<IConnectionMultiplexer>(
            BffCacheConstants.CacheName,
            (_, _) => ConnectionMultiplexer.Connect(connectionString));

        services
            .AddFusionCache()
            .WithDefaultEntryOptions(options =>
            {
                options.Duration = ProductPageTtl;

                // Fail-safe: serve a stale composed page when an upstream is momentarily unavailable
                // (bff.md § 3.1) rather than failing the read.
                options.IsFailSafeEnabled = true;
                options.FailSafeMaxDuration = FailSafeMaxDuration;

                // Anti-stampede on expiry (bff.md § 3.1.1).
                options.JitterMaxDuration = JitterMaxDuration;

                // A volatile-cache hiccup must degrade the read (recompute from upstreams), never 5xx it.
                options.ReThrowDistributedCacheExceptions = false;
                options.ReThrowSerializationExceptions = false;
            })
            .WithSerializer(new FusionCacheSystemTextJsonSerializer())
            .WithDistributedCache(serviceProvider =>
                new RedisCache(new RedisCacheOptions
                {
                    ConnectionMultiplexerFactory = () =>
                        Task.FromResult(serviceProvider.GetRequiredKeyedService<IConnectionMultiplexer>(
                            BffCacheConstants.CacheName)),
                }))
            .WithBackplane(serviceProvider =>
                new RedisBackplane(new RedisBackplaneOptions
                {
                    ConnectionMultiplexerFactory = () =>
                        Task.FromResult(serviceProvider.GetRequiredKeyedService<IConnectionMultiplexer>(
                            BffCacheConstants.CacheName)),
                }));

        return services;
    }
}
