using Basket.Application.Abstractions;
using Basket.Infrastructure.Common.Config;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.CysharpMemoryPack;

namespace Basket.Infrastructure.Persistence;

/// <summary>
/// DI wiring for the Basket aggregate's persistence path: binds
/// <see cref="BasketRedisOptions"/>, registers a keyed singleton
/// <see cref="IConnectionMultiplexer"/> pointed at <c>redis-basket</c>
/// (ADR-0016), registers a named FusionCache <c>"basket"</c> with
/// MemoryPack serialization and the memory level disabled (basket.md &#xa7; 12.1),
/// and wires <see cref="IBasketRepository"/> to
/// <see cref="RedisBasketRepository"/>.
/// </summary>
internal static class PersistenceDependencyInjection
{
    internal static IServiceCollection AddBasketRedisPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptionsWithValidateOnStart<BasketRedisOptions>()
            .Bind(configuration.GetSection(BasketRedisOptions.Section))
            .ValidateDataAnnotations();

        var connectionString = configuration.GetConnectionString("Redis:Basket");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'Redis:Basket' is not configured. " +
                "Basket.Infrastructure requires the 'Redis:Basket' entry " +
                "(redis-basket per ADR-0016).");
        }

        services.AddKeyedSingleton<IConnectionMultiplexer>(
            RedisBasketRepository.BasketCacheName,
            (_, _) => ConnectionMultiplexer.Connect(connectionString));

        services
            .AddFusionCache(RedisBasketRepository.BasketCacheName)
            .WithDefaultEntryOptions(options =>
            {
                // basket.md § 5.3 — sliding 30-day TTL, no eager refresh (baskets must not auto-renew).
                // basket.md § 12.1 — no in-process L1 layer for basket entries.
                options.Duration = TimeSpan.FromDays(30);
                options.EagerRefreshThreshold = null;
                options.IsFailSafeEnabled = false;
                options.SetSkipMemoryCache(true);
            })
            .WithSerializer(new FusionCacheCysharpMemoryPackSerializer())
            .WithDistributedCache(sp =>
                new RedisCache(new RedisCacheOptions
                {
                    ConnectionMultiplexerFactory = () =>
                        Task.FromResult(sp.GetRequiredKeyedService<IConnectionMultiplexer>(
                            RedisBasketRepository.BasketCacheName)),
                }))
            .WithBackplane(sp =>
                new RedisBackplane(new RedisBackplaneOptions
                {
                    ConnectionMultiplexerFactory = () =>
                        Task.FromResult(sp.GetRequiredKeyedService<IConnectionMultiplexer>(
                            RedisBasketRepository.BasketCacheName)),
                }));

        services.AddScoped<IBasketRepository, RedisBasketRepository>();

        return services;
    }
}
