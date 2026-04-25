using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.ConfirmReservation;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Application;

/// <summary>
/// Integration proof that confirm transitions the audit projection + decrements
/// physical stock + emits the external <c>ReservationConfirmedEvent</c> in one
/// atomic transaction.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ConfirmReservationCommandHandlerTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly IntegrationTestFixture _fixture;

    public ConfirmReservationCommandHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TransitionsAuditAndEmitsExternalEventAndDecrementsStock()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using (var seed = _fixture.CreateScope())
        {
            var init = seed.ServiceProvider.GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
            var receive = seed.ServiceProvider.GetRequiredService<ICommandHandler<ReceiveStockCommand>>();
            var reserve = seed.ServiceProvider.GetRequiredService<ICommandHandler<ReserveStockCommand>>();

            (await init.HandleAsync(new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = UtcNow.AddMinutes(-3) }, TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await receive.HandleAsync(new ReceiveStockCommand { ProductId = productId, Quantity = 10, Source = "receiving-dock", OccurredOnUtc = UtcNow.AddMinutes(-2) }, TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await reserve.HandleAsync(new ReserveStockCommand { ProductId = productId, ReservationId = reservationId, OrderId = orderId, Quantity = 4, TimeToLive = TimeSpan.FromMinutes(15), OccurredOnUtc = UtcNow.AddMinutes(-1) }, TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ConfirmReservationCommand>>();

        var result = await handler.HandleAsync(
            new ConfirmReservationCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                OccurredOnUtc = UtcNow,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();

        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Confirmed);
        audit.ResolvedAtUtc.Should().NotBeNull();

        var levels = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        levels.OnHand.Should().Be(6); // 10 - 4 (confirm removes from OnHand)
        levels.Reserved.Should().Be(0);
        levels.Available.Should().Be(6);

        // Two outbox rows keyed by OrderId: StockReservedEvent (from prior
        // reserve step committed in its own tx) + ReservationConfirmedEvent
        // (from this command). Both on inventory.reservations.
        var outboxCount = await db.OutboxMessages
            .AsNoTracking()
            .CountAsync(m => m.KafkaKey == orderId.ToString() && m.TopicName == "inventory.reservations",
                TestContext.Current.CancellationToken);
        outboxCount.Should().Be(2);
    }
}
