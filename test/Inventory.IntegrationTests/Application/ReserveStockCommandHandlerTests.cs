using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.Common.Data;
using Inventory.Application.Common.ReadModels;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Application;

/// <summary>
/// End-to-end Acceptance for <c>ReserveStockCommandHandler</c>. Covers the
/// happy path (event-store append + both projections + outbox write commit
/// atomically), the <c>InsufficientStock</c> business-failure path (outbox
/// carries <c>StockReservationFailedEvent</c>, no stream mutation), and the
/// correlation-id roundtrip from command → <c>stock_events.correlation_id</c>.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class ReserveStockCommandHandlerTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);

    public ReserveStockCommandHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task HappyPath_AppendsEventProjectionsAndOutboxAtomically()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await SeedStreamAsync(productId, onHand: 10);

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReserveStockCommand>>();

        var result = await handler.HandleAsync(
            new ReserveStockCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                Quantity = 3,
                OrderId = orderId,
                TimeToLive = TimeSpan.FromMinutes(15),
                OccurredOnUtc = UtcNow,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // Event-store: 3 events (Init, Receive, Reserve).
        var eventCount = await db.StockEvents
            .AsNoTracking()
            .CountAsync(r => r.StreamId == productId, TestContext.Current.CancellationToken);
        eventCount.Should().Be(3);

        // CurrentStockLevels projection reflects the reserve.
        var levels = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        levels.OnHand.Should().Be(10);
        levels.Reserved.Should().Be(3);
        levels.Available.Should().Be(7);

        // Reservation audit row is inserted Active.
        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Active);
        audit.OrderId.Should().Be(orderId);
        audit.Quantity.Should().Be(3);

        // Outbox has the external StockReservedEvent keyed by OrderId.
        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle(m => m.TopicName == "inventory.reservations");
    }

    [Fact]
    public async Task InsufficientStock_EmitsFailureEventAndAppendsNoStockEvent()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Seed only 2 units, then request 5.
        await SeedStreamAsync(productId, onHand: 2);

        var eventCountBefore = await CountStockEventsAsync(productId);

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReserveStockCommand>>();

        var result = await handler.HandleAsync(
            new ReserveStockCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                Quantity = 5,
                OrderId = orderId,
                TimeToLive = TimeSpan.FromMinutes(15),
                OccurredOnUtc = UtcNow,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<InsufficientStockError>()
            .Which.ErrorCode.Should().Be("Inventory.InsufficientStock");

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // Stock-events unchanged — the reserve was rejected before append.
        var eventCountAfter = await db.StockEvents
            .AsNoTracking()
            .CountAsync(r => r.StreamId == productId, TestContext.Current.CancellationToken);
        eventCountAfter.Should().Be(eventCountBefore);

        // No audit row inserted for the failed reservation.
        var auditRow = await db.ReservationAudit
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        auditRow.Should().BeNull();

        // Outbox carries StockReservationFailedEvent keyed by OrderId.
        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle(m => m.TopicName == "inventory.reservations");
    }

    [Fact]
    public async Task CorrelationIdRoundtripsFromCommandToStockEventsRow()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await SeedStreamAsync(productId, onHand: 10);

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReserveStockCommand>>();

        var result = await handler.HandleAsync(
            new ReserveStockCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                Quantity = 1,
                OrderId = orderId,
                TimeToLive = TimeSpan.FromMinutes(15),
                OccurredOnUtc = UtcNow,
                CorrelationId = correlationId,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var reserveRow = await db.StockEvents
            .AsNoTracking()
            .Where(r => r.StreamId == productId && r.EventType == "StockReservedEvent")
            .OrderByDescending(r => r.Version)
            .FirstAsync(TestContext.Current.CancellationToken);

        reserveRow.CorrelationId.Should().Be(correlationId);
    }

    private async Task SeedStreamAsync(Guid productId, int onHand)
    {
        using var seedScope = Fixture.CreateScope();
        var initHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
        var receiveHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();

        (await initHandler.HandleAsync(
            new InitializeStockItemCommand
            {
                ProductId = productId,
                OccurredOnUtc = UtcNow.AddMinutes(-2),
            },
            TestContext.Current.CancellationToken)).Should().BeSuccess();

        if (onHand > 0)
        {
            (await receiveHandler.HandleAsync(
                new ReceiveStockCommand
                {
                    ProductId = productId,
                    Quantity = onHand,
                    Source = "receiving-dock",
                    ReceivedByUserId = null,
                    OccurredOnUtc = UtcNow.AddMinutes(-1),
                },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }
    }

    private async Task<int> CountStockEventsAsync(Guid productId)
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.StockEvents
            .AsNoTracking()
            .CountAsync(r => r.StreamId == productId, TestContext.Current.CancellationToken);
    }
}
