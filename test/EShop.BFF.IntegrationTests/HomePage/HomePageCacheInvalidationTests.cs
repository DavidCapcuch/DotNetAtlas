using Avro;
using Catalog.Categories;
using Catalog.Products;
using EShop.BFF.IntegrationTests.Common;
using Inventory.Stock;
using Platform.Test.Framework.Common;

namespace EShop.BFF.IntegrationTests.HomePage;

/// <summary>
/// The live <c>bff-group</c> cache invalidator over Testcontainers Kafka + Schema Registry (issue #328
/// acceptance): a real Avro event produced to each of the three subscribed topics evicts the seeded
/// <c>home-page</c> cache entry — proving the consume → <c>RemoveByTag("home-page")</c> path end-to-end.
/// </summary>
[Collection<CacheInvalidationTestCollection>]
public sealed class HomePageCacheInvalidationTests(CacheInvalidationTestFixture fixture)
{
    private static readonly TimeSpan EvictionTimeout = TimeSpan.FromSeconds(30);

    private readonly CacheInvalidationTestFixture _fixture = fixture;

    [Fact]
    public async Task ProductPriceChangedEvent_OnCatalogProducts_EvictsTheHomePage()
    {
        var productId = Guid.NewGuid();
        var @event = new ProductPriceChangedEvent
        {
            ProductId = productId,
            Sku = "SKU-1",
            // Scale must match the schema's decimal(19,4) — a scale-0 literal fails to encode.
            OldPriceAmount = new AvroDecimal(10.0000m),
            NewPriceAmount = new AvroDecimal(12.0000m),
            Currency = "USD",
            ChangedAtUtc = DateTime.UtcNow,
        };

        await AssertEvictsHomePageAsync(
            CacheInvalidationTestFixture.CatalogProductsTopic, productId, @event);
    }

    [Fact]
    public async Task CategoryCreatedEvent_OnCatalogCategories_EvictsTheHomePage()
    {
        var categoryId = Guid.NewGuid();
        var @event = new CategoryCreatedEvent
        {
            CategoryId = categoryId,
            Name = "Electronics",
            ParentCategoryId = null,
            Path = "/electronics",
            CreatedAtUtc = DateTime.UtcNow,
        };

        await AssertEvictsHomePageAsync(
            CacheInvalidationTestFixture.CatalogCategoriesTopic, categoryId, @event);
    }

    [Fact]
    public async Task StockLevelChangedEvent_OnInventoryStockEvents_EvictsTheHomePage()
    {
        var productId = Guid.NewGuid();
        var @event = new StockLevelChangedEvent
        {
            ProductId = productId,
            NewOnHand = 10,
            NewReserved = 2,
            NewAvailable = 8,
            ChangedAtUtc = DateTime.UtcNow,
        };

        await AssertEvictsHomePageAsync(
            CacheInvalidationTestFixture.InventoryStockEventsTopic, productId, @event);
    }

    private async Task AssertEvictsHomePageAsync(string topic, Guid key, Avro.Specific.ISpecificRecord @event)
    {
        // Arrange
        await _fixture.SeedHomePageCacheAsync();
        (await _fixture.IsHomePageCachedAsync(TestContext.Current.CancellationToken))
            .Should().BeTrue("the home page was just seeded");

        // Act
        await _fixture.ProduceAsync(topic, key, @event);

        // Assert — the live consumer removes the home-page tag within the timeout.
        await Eventually.UntilAsync(
            async token => !await _fixture.IsHomePageCachedAsync(token),
            EvictionTimeout,
            "the bff-group consumer to evict the home-page tag on the event",
            TestContext.Current.CancellationToken);

        // A cache read the deadline cancelled, or one whose L2 fault was swallowed, both report a
        // miss — which the negated probe above reads as an eviction. Re-read on the test's own token
        // so a pass means the entry is gone rather than merely unreadable.
        (await _fixture.IsHomePageCachedAsync(TestContext.Current.CancellationToken))
            .Should().BeFalse("the consumer must have removed the entry, not just failed to read it");
    }
}
