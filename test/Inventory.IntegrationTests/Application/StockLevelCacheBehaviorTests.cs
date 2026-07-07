using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.GetStockLevelByProductId;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.Errors;
using Inventory.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Application;

/// <summary>
/// Behavioural acceptance for the ADR-0034 read-through cache: the projection handler
/// evicts the display key on every applied event (invalidate-on-projection-update), and the
/// reservation decision path is oversell-safe by construction — it reads the event-sourced
/// aggregate, never the display cache, so even a deliberately poisoned cache cannot cause a
/// double-sell.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class StockLevelCacheBehaviorTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 5, 2, 10, 0, 0, TimeSpan.Zero);

    public StockLevelCacheBehaviorTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ProjectionUpdate_EvictsCachedDisplayKey()
    {
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 5, UtcNow, TestContext.Current.CancellationToken);

        // Display read warms the cache.
        (await ReadSingleAsync(productId)).Should().BeSuccess();
        Fixture.StockLevelCache.Contains(productId).Should().BeTrue("the display read backfills the cache");

        // A stock movement updates the projection — the projection handler must evict the key.
        await Seed.ReceiveAsync(productId, quantity: 3, UtcNow.AddMinutes(5), TestContext.Current.CancellationToken);
        Fixture.StockLevelCache.Contains(productId).Should().BeFalse(
            "the projection handler evicts the product's inventory:stock cache key in the same flow as the upsert (ADR-0034)");

        // Next display read rebuilds from the fresh projection row.
        var rebuilt = await ReadSingleAsync(productId);
        rebuilt.Should().BeSuccess();
        rebuilt.Value.Available.Should().Be(8, "5 on hand + 3 received");
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task StaleCache_CannotCauseOversell()
    {
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 5, UtcNow, TestContext.Current.CancellationToken);

        // Poison the display cache with a wildly-high availability. If the reservation path
        // read the cache, reserving 10 would (wrongly) succeed.
        Fixture.StockLevelCache.Poison(new StockLevelResponse
        {
            ProductId = productId,
            OnHand = 9999,
            Reserved = 0,
            Available = 9999,
            LastUpdatedUtc = UtcNow,
            LastVersion = 2,
        });

        using var scope = Fixture.CreateScope();
        var reserve = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReserveStockCommand>>();

        var result = await reserve.HandleAsync(
            new ReserveStockCommand
            {
                ReservationId = Guid.CreateVersion7(),
                ProductId = productId,
                Quantity = 10,
                OrderId = Guid.CreateVersion7(),
                TimeToLive = TimeSpan.FromMinutes(15),
                OccurredOnUtc = UtcNow.AddMinutes(1),
            },
            TestContext.Current.CancellationToken);

        // The aggregate (Available = 5) rejects the over-reservation regardless of the
        // poisoned cache — oversell-safe by construction (ADR-0034 / ADR-0006).
        result.Should().BeFailure();
        result.Errors.Should().ContainItemsAssignableTo<InsufficientStockError>();
    }

    private async Task<FluentResults.Result<StockLevelResponse>> ReadSingleAsync(Guid productId)
    {
        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetStockLevelByProductIdQuery, StockLevelResponse>>();

        return await handler.HandleAsync(
            new GetStockLevelByProductIdQuery { ProductId = productId },
            TestContext.Current.CancellationToken);
    }
}
