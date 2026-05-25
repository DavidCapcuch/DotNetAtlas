using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.StockInit;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;
using AvroOrderStatusAtTransition = Ordering.Orders.OrderStatusAtTransition;

namespace Inventory.IntegrationTests.Messaging.Kafka;

/// <summary>
/// M5 acceptance for <see cref="OrderCancelledEventKafkaHandler"/>. The
/// non-trivial fan-out path: an <c>OrderCancelledEvent</c> with two Active
/// reservations on different products fans out into two
/// <c>ReleaseReservationCommand</c>s with
/// <see cref="ReleaseReason.Cancellation"/>. Asserts both audit rows flip
/// to <c>Released</c>, both products' Available climbs back, and two
/// <c>ReservationReleasedEvent</c> outbox rows land — one per product.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class OrderCancelledEventKafkaHandlerTests : BaseIntegrationTest
{
    private static readonly DateTime UtcNow =
        new(2026, 4, 25, 14, 0, 0, DateTimeKind.Utc);

    public OrderCancelledEventKafkaHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task TwoActiveReservations_BothReleased()
    {
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var productAId = Guid.NewGuid();
        var productBId = Guid.NewGuid();
        var reservationAId = Guid.NewGuid();
        var reservationBId = Guid.NewGuid();

        await SeedActiveReservationAsync(productAId, reservationAId, orderId, quantity: 2);
        await SeedActiveReservationAsync(productBId, reservationBId, orderId, quantity: 5);

        var avroEvent = new AvroOrderCancelledEvent
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = Guid.NewGuid(),
            Reason = "customer requested cancel",
            AtStatus = AvroOrderStatusAtTransition.StockReserved,
            CancelledAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<OrderCancelledEventKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            origin: "Ordering",
            cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(context, avroEvent);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var auditRows = await db.ReservationAudit
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .ToListAsync(TestContext.Current.CancellationToken);
        auditRows.Should().HaveCount(2);
        auditRows.Should().AllSatisfy(r =>
        {
            r.Status.Should().Be(ReservationStatus.Released);
            r.ReleaseReason.Should().Be(ReleaseReason.Cancellation);
        });

        // Each product's stream regains its full Available capacity.
        var levelsA = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productAId, TestContext.Current.CancellationToken);
        levelsA.Reserved.Should().Be(0);
        levelsA.Available.Should().Be(10);

        var levelsB = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productBId, TestContext.Current.CancellationToken);
        levelsB.Reserved.Should().Be(0);
        levelsB.Available.Should().Be(10);

        // Two ReservationReleasedEvent outbox rows -- one per product, both
        // keyed by the order id (per inventory.reservations topic key contract).
        var releasedOutboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == "Inventory.Reservations.ReservationReleasedEvent")
            .ToListAsync(TestContext.Current.CancellationToken);
        releasedOutboxRows.Should().HaveCount(2);
        releasedOutboxRows.Should().AllSatisfy(m =>
            m.TopicName.Should().Be("inventory.reservations"));
    }

    [Fact]
    public async Task NoActiveReservations_NoOp()
    {
        var orderId = Guid.NewGuid();

        var avroEvent = new AvroOrderCancelledEvent
        {
            OrderId = orderId,
            CorrelationId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            Reason = "no reservations on this order",
            AtStatus = AvroOrderStatusAtTransition.Created,
            CancelledAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<OrderCancelledEventKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            origin: "Ordering",
            cancellationToken: TestContext.Current.CancellationToken);

        var act = async () => await handler.Handle(context, avroEvent);
        await act.Should().NotThrowAsync();

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var outboxRowsForOrder = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRowsForOrder.Should().BeEmpty();
    }

    private async Task SeedActiveReservationAsync(Guid productId, Guid reservationId, Guid orderId, int quantity)
    {
        var seedUtc = new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5);

        using var seedScope = Fixture.CreateScope();
        var initHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
        var receiveHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();
        var reserveHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<ReserveStockCommand>>();

        await initHandler.HandleAsync(
            new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = seedUtc },
            TestContext.Current.CancellationToken);

        await receiveHandler.HandleAsync(
            new ReceiveStockCommand
            {
                ProductId = productId,
                Quantity = 10,
                Source = "receiving-dock",
                ReceivedByUserId = null,
                OccurredOnUtc = seedUtc.AddMinutes(1),
            },
            TestContext.Current.CancellationToken);

        await reserveHandler.HandleAsync(
            new ReserveStockCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                Quantity = quantity,
                OrderId = orderId,
                TimeToLive = TimeSpan.FromMinutes(15),
                OccurredOnUtc = seedUtc.AddMinutes(2),
            },
            TestContext.Current.CancellationToken);
    }
}
