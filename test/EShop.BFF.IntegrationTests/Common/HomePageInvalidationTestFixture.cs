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

internal sealed class HomePageInvalidationTestCollection : TestCollection<HomePageInvalidationTestFixture>;

/// <summary>
/// Boots the BFF over real <c>redis-cache</c> + Kafka + Schema Registry Testcontainers and starts the
/// live <c>bff-group</c> cache-invalidation consumer (Program skips the bus in the test host, so the
/// fixture starts it explicitly). Exercises the real produce → consume → <c>RemoveByTag</c> path: a
/// cache-invalidation event evicts the seeded <c>home-page</c> entry. No WireMock — the invalidator makes
/// no upstream calls; the eager-warm hosted service is disabled so it can't repopulate the cache mid-test.
/// </summary>
[DisableWafCache]
public sealed class HomePageInvalidationTestFixture : AppFixture<Program>
{
    public const string CatalogProductsTopic = "catalog.products";
    public const string CatalogCategoriesTopic = "catalog.categories";
    public const string InventoryStockEventsTopic = "inventory.stock-events";

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    private KafkaTestProducer _producer = null!;
    private IKafkaBus _kafkaBus = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();
        await _kafkaContainer.CreateKafkaTopicsAsync(
            [CatalogProductsTopic, CatalogCategoriesTopic, InventoryStockEventsTopic]);
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
