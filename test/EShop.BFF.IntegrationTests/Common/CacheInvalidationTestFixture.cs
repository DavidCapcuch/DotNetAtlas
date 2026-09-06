using Avro.Specific;
using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Messaging.Config;
using FastEndpoints.Testing;
using KafkaFlow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Test.Framework;
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
    /// <summary>
    /// Read from the BFF's own <c>appsettings.json</c> rather than spelled here. The readiness
    /// check verifies this exact set, so a literal that drifted from configuration would turn the
    /// probe red naming a topic no test mentions.
    /// </summary>
    private static readonly BffTopicsOptions Topics = LoadTopicsFromConfiguration();

    public static string CatalogProductsTopic => Topics.CatalogProducts;
    public static string CatalogCategoriesTopic => Topics.CatalogCategories;
    public static string InventoryStockEventsTopic => Topics.InventoryStockEvents;
    public static string BasketSessionsTopic => Topics.BasketSessions;

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    private KafkaTestProducer _producer = null!;
    private IKafkaBus _kafkaBus = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();
        await _kafkaContainer.CreateKafkaTopicsAsync(Topics.GetAllTopics());
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

    private static BffTopicsOptions LoadTopicsFromConfiguration()
    {
        var bffApiPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(), "src", "EShop.BFF", "EShop.BFF.Api");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(bffApiPath)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        return configuration.GetSection(BffTopicsOptions.Section).Get<BffTopicsOptions>()
               ?? throw new InvalidOperationException(
                   $"Failed to bind configuration section '{BffTopicsOptions.Section}' to "
                   + $"{nameof(BffTopicsOptions)}. Verify appsettings.json carries the topic values.");
    }
}
