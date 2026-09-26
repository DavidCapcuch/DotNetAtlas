using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.GetStockLevelsBulk;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Inventory.IntegrationTests.ApiEndpoints.StockItems;

/// <summary>
/// Integration coverage for <c>POST /api/v1/inventory/stock-items/bulk</c> (ADR-0034)
/// against real <c>redis-cache</c>: anonymous partial-tolerant reads, validation, the
/// FusionCache read-through landing in Redis, and invalidate-on-projection-update keeping
/// the display fresh well inside the TTL.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetStockLevelsBulkTests : BaseIntegrationTest
{
    private const string BulkRoute = "/api/v1/inventory/stock-items/bulk";

    private static readonly DateTimeOffset SeedUtc = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    public GetStockLevelsBulkTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenAnonymous_AndMixOfProducts_Returns200WithItemsAndMissing()
    {
        var known = Guid.CreateVersion7();
        var unknown = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(known, onHand: 6, SeedUtc, TestContext.Current.CancellationToken);

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = new[] { known, unknown } }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetStockLevelsBulkResponse>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        using (new AssertionScope())
        {
            body!.Items.Should().ContainSingle(i => i.ProductId == known).Which.Available.Should().Be(6);
            body.MissingProductIds.Should().ContainSingle().Which.Should().Be(unknown);
        }
    }

    // The two edges of the partial-tolerant contract below pin the serialized shape too — an empty
    // missingProductIds / items array on the wire, never a null.
    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenAllProductsKnown_Returns200WithEveryItem_AndNoMissing()
    {
        // Arrange
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(first, onHand: 4, SeedUtc, TestContext.Current.CancellationToken);
        await Seed.ProductWithOnHandAsync(second, onHand: 9, SeedUtc, TestContext.Current.CancellationToken);

        // Act
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = new[] { first, second } }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GetStockLevelsBulkResponse>(TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.MissingProductIds.Should().NotBeNull().And.BeEmpty();
            body.Items.Should().HaveCount(2);
            body.Items.Should().ContainSingle(i => i.ProductId == first).Which.Available.Should().Be(4);
            body.Items.Should().ContainSingle(i => i.ProductId == second).Which.Available.Should().Be(9);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenAllProductsUnknown_Returns200WithNoItems_AndAllMissing()
    {
        // Arrange
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        // Act
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = new[] { first, second } }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GetStockLevelsBulkResponse>(TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().NotBeNull().And.BeEmpty();
            body.MissingProductIds.Should().BeEquivalentTo([first, second]);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenEmptyList_Returns422()
    {
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = Array.Empty<Guid>() }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenExceedsMaxProductIds_Returns422()
    {
        var ids = Enumerable.Range(0, GetStockLevelsBulkQueryValidator.MaxProductIds + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = ids }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Read_PopulatesRedisCache()
    {
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 3, SeedUtc, TestContext.Current.CancellationToken);

        await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = new[] { productId } }, TestContext.Current.CancellationToken);

        RedisKeyExistsFor(productId).Should().BeTrue(
            "the read-through cache stores the row in redis-cache under the inventory:stock namespace");
    }

    [Fact]
    public async Task RepeatRead_IsServedFromCache_NotTheProjectionRow()
    {
        // Arrange — warm the read-through cache, then change the projection row behind it. A raw
        // UPDATE appends no stock event, so nothing evicts the cached entry.
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 5, SeedUtc, TestContext.Current.CancellationToken);
        await ReadAvailableAsync(productId);

        var updatedRows = await InventoryDbContext.CurrentStockLevels
            .Where(r => r.ProductId == productId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.OnHand, 1).SetProperty(r => r.Available, 1),
                TestContext.Current.CancellationToken);
        updatedRows.Should().Be(1, "the projection row must change behind the cache");

        // Act
        var available = await ReadAvailableAsync(productId);

        // Assert — ADR-0034: inside the TTL the warmed entry answers, not current_stock_levels.
        available.Should().Be(5);
    }

    [Fact]
    public async Task ProjectionUpdate_EvictsCache_SoSubsequentReadIsFresh()
    {
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 5, SeedUtc, TestContext.Current.CancellationToken);

        // Warm the cache (available = 5).
        (await ReadAvailableAsync(productId)).Should().Be(5);

        // Mutate stock through the API — the projection handler evicts the cache key.
        var receive = await Fixture.HttpClientRegistry.CommandsClient
            .PostAsJsonAsync(
                $"/api/v1/inventory/stock-items/{productId}/receive",
                new { ProductId = productId, Quantity = 4, Source = "receiving-dock" },
                TestContext.Current.CancellationToken);
        receive.StatusCode.Should().Be(HttpStatusCode.OK);

        // Immediately (well within the 30s TTL) the read reflects the new level — only
        // possible if the stale entry was evicted, not waited out (ADR-0034).
        (await ReadAvailableAsync(productId)).Should().Be(9, "5 on hand + 4 received, served fresh after eviction");
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task CorruptCachedPayload_DegradesToProjection_NotError()
    {
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 3, SeedUtc, TestContext.Current.CancellationToken);

        // Warm the cache, then corrupt the stored payload in redis-cache (simulates an
        // incompatible MemoryPack shape left across a deploy). The read must treat it as a
        // miss and rebuild from the projection — graceful degradation, never a 5xx (ADR-0034).
        (await ReadAvailableAsync(productId)).Should().Be(3);

        await Fixture.RedisMultiplexer.GetDatabase()
            .StringSetAsync(FindRedisKeyFor(productId), "not-a-valid-memorypack-payload");

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = new[] { productId } }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetStockLevelsBulkResponse>(TestContext.Current.CancellationToken);
        body!.Items.Single(i => i.ProductId == productId).Available.Should().Be(3, "the corrupt entry is treated as a miss and rebuilt from current_stock_levels");
    }

    private async Task<int> ReadAvailableAsync(Guid productId)
    {
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = new[] { productId } }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetStockLevelsBulkResponse>(TestContext.Current.CancellationToken);
        return body!.Items.Single(i => i.ProductId == productId).Available;
    }

    private bool RedisKeyExistsFor(Guid productId)
    {
        var endpoint = Fixture.RedisMultiplexer.GetEndPoints()[0];
        var server = Fixture.RedisMultiplexer.GetServer(endpoint);
        return server.Keys(pattern: $"*{productId:D}*").Any();
    }

    private StackExchange.Redis.RedisKey FindRedisKeyFor(Guid productId)
    {
        var endpoint = Fixture.RedisMultiplexer.GetEndPoints()[0];
        var server = Fixture.RedisMultiplexer.GetServer(endpoint);
        return server.Keys(pattern: $"*{productId:D}*").First();
    }
}
