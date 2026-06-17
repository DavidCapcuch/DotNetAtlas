using Avro.Specific;
using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Caching;
using FastEndpoints.Testing;
using KafkaFlow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.IntegrationTests.Common;

internal sealed class CacheInvalidationTestCollection : TestCollection<CacheInvalidationTestFixture>;

/// <summary>
/// Boots the BFF over real <c>redis-cache</c> + Kafka + Schema Registry Testcontainers and starts the live
/// <c>bff-group</c> cache-invalidation consumer (Program skips the bus in the test host, so the fixture
/// starts it explicitly). Exercises the real produce → consume → <c>RemoveByTag</c> path end-to-end for
/// both invalidation families: Catalog / Inventory events evict the <c>home-page</c> entry, and
/// <c>BasketCheckoutInitiatedEvent</c> evicts a buyer's <c>basket-bff-{UserId}</c> entry. No WireMock — the
/// invalidator makes no upstream calls; the eager-warm hosted service is disabled so it can't repopulate
/// the cache mid-test. All four subscribed topics are pre-created (the consumer subscribes to all four).
/// </summary>
[DisableWafCache]
public sealed class CacheInvalidationTestFixture : AppFixture<Program>
{
    public const string CatalogProductsTopic = "catalog.products";
    public const string CatalogCategoriesTopic = "catalog.categories";
    public const string InventoryStockEventsTopic = "inventory.stock-events";
    public const string BasketSessionsTopic = "basket.sessions";

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    private KafkaTestProducer _producer = null!;
    private IKafkaBus _kafkaBus = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();
        await _kafkaContainer.CreateKafkaTopicsAsync(
            [CatalogProductsTopic, CatalogCategoriesTopic, InventoryStockEventsTopic, BasketSessionsTopic]);
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Redis:Cache", _redisContainer.ConfigurationOptions.ToString())
                .UseKafkaSettings(_kafkaContainer.KafkaOptions)
                // Read from the start so the consumer sees an event produced right after it joins the group.
                .UseSetting("KafkaBffCacheInvalidationConsumer:AutoOffsetReset", "Earliest")
                .UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        });

        return base.ConfigureAppHost(a);
    }

    // Warm off so the warmer can't repopulate home-page:v1 mid-test.
    protected override void ConfigureApp(IWebHostBuilder a) =>
        a.UseEnvironment("Testing").UseTestSerilog().UseWarmFlag(enabled: false);

    protected override async ValueTask SetupAsync()
    {
        _producer = new KafkaTestProducer(_kafkaContainer.KafkaOptions);

        // Program intentionally skips the bus in the test host — start it here so the consumer runs live.
        _kafkaBus = Services.CreateKafkaBus();
        await _kafkaBus.StartAsync();
    }

    /// <summary>Writes a <c>home-page:v1</c> entry tagged <c>home-page</c> (the invalidation target).</summary>
    public async Task SeedHomePageCacheAsync()
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        var page = new HomePageResponse
        {
            FeaturedProducts = [],
            CategoryTree = null,
            StockHighlights = null,
            HasStaleData = false,
            GeneratedAtUtc = DateTimeOffset.UnixEpoch,
        };

        await cache.SetAsync(BffCacheConstants.HomePageKey, page, tags: BffHomePageCache.Tags);
    }

    public async Task<bool> IsHomePageCachedAsync()
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        var maybe = await cache.TryGetAsync<HomePageResponse>(BffCacheConstants.HomePageKey);
        return maybe.HasValue;
    }

    /// <summary>Writes a <c>basket-bff:{userId}</c> entry tagged <c>basket-bff-{userId}</c> (the invalidation target).</summary>
    public async Task SeedBasketCacheAsync(Guid userId)
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        var page = new BasketPageResponse
        {
            UserId = userId,
            Version = 1,
            Items = [],
            TotalSnapshot = new MoneyDto(0m, "USD"),
            TotalCurrent = new MoneyDto(0m, "USD"),
            HasPriceDrift = false,
            HasOutOfStock = false,
            HasStaleData = false,
            GeneratedAtUtc = DateTimeOffset.UnixEpoch,
        };

        await cache.SetAsync(
            BffCacheConstants.BasketPageKey(userId), page, tags: BffBasketCache.Tags(userId));
    }

    public async Task<bool> IsBasketCachedAsync(Guid userId)
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        var maybe = await cache.TryGetAsync<BasketPageResponse>(BffCacheConstants.BasketPageKey(userId));
        return maybe.HasValue;
    }

    public Task ProduceAsync(string topic, Guid key, ISpecificRecord value) =>
        _producer.ProduceAsync(topic, key, value);

    protected override async ValueTask TearDownAsync()
    {
        if (_kafkaBus is not null)
        {
            await _kafkaBus.StopAsync();
        }

        _producer?.Dispose();
        await _kafkaContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
