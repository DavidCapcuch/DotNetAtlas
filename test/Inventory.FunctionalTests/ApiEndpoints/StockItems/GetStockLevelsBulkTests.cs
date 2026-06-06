using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.GetStockLevelsBulk;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.FunctionalTests.Common;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Inventory.FunctionalTests.ApiEndpoints.StockItems;

/// <summary>
/// End-to-end coverage for <c>POST /api/v1/inventory/stock-items/bulk</c> (ADR-0034)
/// against real <c>redis-cache</c>: anonymous partial-tolerant reads, validation, the
/// FusionCache read-through landing in Redis, and invalidate-on-projection-update keeping
/// the display fresh well inside the TTL.
/// </summary>
[Collection<FunctionalTestCollection>]
public sealed class GetStockLevelsBulkTests : BaseApiTest
{
    private const string BulkRoute = "/api/v1/inventory/stock-items/bulk";

    public GetStockLevelsBulkTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenAnonymous_AndMixOfProducts_Returns200WithItemsAndMissing()
    {
        var known = Guid.CreateVersion7();
        var unknown = Guid.CreateVersion7();
        await SeedStreamAsync(known, onHand: 6);

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

    [Fact]
    public async Task WhenEmptyList_Returns422()
    {
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = Array.Empty<Guid>() }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
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
        await SeedStreamAsync(productId, onHand: 3);

        await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync(BulkRoute, new { productIds = new[] { productId } }, TestContext.Current.CancellationToken);

        RedisKeyExistsFor(productId).Should().BeTrue(
            "the read-through cache stores the row in redis-cache under the inventory:stock namespace");
    }

    [Fact]
    public async Task ProjectionUpdate_EvictsCache_SoSubsequentReadIsFresh()
    {
        var productId = Guid.CreateVersion7();
        await SeedStreamAsync(productId, onHand: 5);

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
    public async Task CorruptCachedPayload_DegradesToProjection_NotError()
    {
        var productId = Guid.CreateVersion7();
        await SeedStreamAsync(productId, onHand: 3);

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

    private async Task SeedStreamAsync(Guid productId, int onHand)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var init = scope.ServiceProvider.GetRequiredService<Platform.CQRS.ICommandHandler<InitializeStockItemCommand>>();
        var receive = scope.ServiceProvider.GetRequiredService<Platform.CQRS.ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();
        (await init.HandleAsync(
            new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = DateTimeOffset.UtcNow.AddMinutes(-2) },
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        (await receive.HandleAsync(
            new ReceiveStockCommand
            {
                ProductId = productId,
                Quantity = onHand,
                Source = "receiving-dock",
                ReceivedByUserId = null,
                OccurredOnUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
    }
}
