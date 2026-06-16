using Avro;
using Catalog.Categories;
using Catalog.Products;
using EShop.BFF.IntegrationTests.Common;
using Inventory.Stock;

namespace EShop.BFF.IntegrationTests.HomePage;

/// <summary>
/// The live <c>bff-group</c> cache invalidator over Testcontainers Kafka + Schema Registry (issue #328
/// acceptance): a real Avro event produced to each of the three subscribed topics evicts the seeded
/// <c>home-page</c> cache entry — proving the consume → <c>RemoveByTag("home-page")</c> path end-to-end.
/// </summary>
[Collection<HomePageInvalidationTestCollection>]
public sealed class HomePageCacheInvalidationTests(HomePageInvalidationTestFixture fixture)
{
    private static readonly TimeSpan EvictionTimeout = TimeSpan.FromSeconds(30);

    private readonly HomePageInvalidationTestFixture _fixture = fixture;

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
            HomePageInvalidationTestFixture.CatalogProductsTopic, productId, @event);
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
            HomePageInvalidationTestFixture.CatalogCategoriesTopic, categoryId, @event);
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
            HomePageInvalidationTestFixture.InventoryStockEventsTopic, productId, @event);
    }

    private async Task AssertEvictsHomePageAsync(string topic, Guid key, Avro.Specific.ISpecificRecord @event)
    {
        // Arrange
        await _fixture.SeedHomePageCacheAsync();
        (await _fixture.IsHomePageCachedAsync()).Should().BeTrue("the home page was just seeded");

        // Act
        await _fixture.ProduceAsync(topic, key, @event);

        // Assert — the live consumer removes the home-page tag within the timeout.
        var evicted = await EventuallyEvictedAsync();
        evicted.Should().BeTrue("the bff-group consumer should remove the home-page tag on the event");
    }

    private async Task<bool> EventuallyEvictedAsync()
    {
        var deadline = DateTime.UtcNow + EvictionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!await _fixture.IsHomePageCachedAsync())
            {
                return true;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return !await _fixture.IsHomePageCachedAsync();
    }
}
