using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Application;

/// <summary>
/// Proves the threshold-crossing rule from <c>inventory.md</c> § 6.1:
/// <c>StockLevelChanged</c> fires ONLY on <c>0 &lt;-&gt; positive</c>
/// transitions, never on every stock movement.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class StockLevelChangedEmissionTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);

    public StockLevelChangedEmissionTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task OnlyFiresOnZeroToPositiveAndPositiveToZeroTransitions()
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using var scope = Fixture.CreateScope();
        var init = scope.ServiceProvider.GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
        var receive = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();
        var reserve = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReserveStockCommand>>();

        // Init — Available stays 0 (never positive); NO emission.
        (await init.HandleAsync(new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = UtcNow }, TestContext.Current.CancellationToken)).Should().BeSuccess();

        // Receive 5 — Available 0 -> 5; EMIT.
        (await receive.HandleAsync(new ReceiveStockCommand { ProductId = productId, Quantity = 5, Source = "receiving-dock", OccurredOnUtc = UtcNow.AddSeconds(1) }, TestContext.Current.CancellationToken)).Should().BeSuccess();

        // Reserve 2 — Available 5 -> 3; NO emission (positive to positive).
        (await reserve.HandleAsync(new ReserveStockCommand { ProductId = productId, ReservationId = Guid.NewGuid(), OrderId = orderId, Quantity = 2, TimeToLive = TimeSpan.FromMinutes(15), OccurredOnUtc = UtcNow.AddSeconds(2) }, TestContext.Current.CancellationToken)).Should().BeSuccess();

        // Receive 1 — Available 3 -> 4; NO emission.
        (await receive.HandleAsync(new ReceiveStockCommand { ProductId = productId, Quantity = 1, Source = "receiving-dock", OccurredOnUtc = UtcNow.AddSeconds(3) }, TestContext.Current.CancellationToken)).Should().BeSuccess();

        // Reserve 4 — Available 4 -> 0; EMIT.
        (await reserve.HandleAsync(new ReserveStockCommand { ProductId = productId, ReservationId = Guid.NewGuid(), OrderId = orderId, Quantity = 4, TimeToLive = TimeSpan.FromMinutes(15), OccurredOnUtc = UtcNow.AddSeconds(4) }, TestContext.Current.CancellationToken)).Should().BeSuccess();

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stockEventOutboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == productId.ToString()
                && m.TopicName == "inventory.stock-events"
                && m.Type == "Inventory.Stock.StockLevelChanged")
            .ToListAsync(TestContext.Current.CancellationToken);

        stockEventOutboxRows.Should().HaveCount(2,
            "exactly two 0<->positive transitions occurred across the sequence (0->5 and 4->0)");
    }
}
