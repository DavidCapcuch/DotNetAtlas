using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Platform.Test.Framework.Assertions;
using Platform.Test.Framework.Kafka;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;

namespace Inventory.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Slice tests for reserving stock, entered through the saga's message: the
/// <see cref="ReserveStockCommandKafkaHandler"/> is invoked directly with a synthetic
/// <see cref="FakeKafkaMessageContext"/> and the real Avro <see cref="AvroReserveStockCommand"/>, so
/// the Avro→application-command translation is exercised too. Reserve has no HTTP endpoint, so this
/// is its public entrance.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class ReserveStockCommandKafkaHandlerTests : BaseIntegrationTest
{
    private static readonly DateTime UtcNow =
        new(2026, 4, 25, 10, 0, 0, DateTimeKind.Utc);

    public ReserveStockCommandKafkaHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task HappyPath_AvroCommandTranslatedAndDispatched()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ProductWithOnHandAsync(
            productId,
            onHand: 10,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-2),
            TestContext.Current.CancellationToken);

        // Act
        await Fixture.DispatchAsync<ReserveStockCommandKafkaHandler, AvroReserveStockCommand>(new AvroReserveStockCommand
        {
            OrderId = orderId,
            ProductId = productId,
            ReservationId = reservationId,
            Quantity = 3,
            RequestedAtUtc = UtcNow,
        });

        // Assert — the reserve reached the stream, both projections and the outbox.
        var audit = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        var streamLength = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(e => e.StreamId == productId, TestContext.Current.CancellationToken);
        var levels = await InventoryDbContext.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        var outboxRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            audit.Status.Should().Be(ReservationStatus.Active);
            audit.OrderId.Should().Be(orderId);
            audit.Quantity.Should().Be(3);

            streamLength.Should().Be(3, "Initialize + Receive seeded, then this Reserve");
            levels.OnHand.Should().Be(10);
            levels.Reserved.Should().Be(3);
            levels.Available.Should().Be(7);

            outboxRows.Should().ContainSingle(m => m.TopicName == "inventory.reservations")
                .Which.Type.Should().BeMessageType<Inventory.Reservations.StockReservedEvent>();
        }
    }

    [Fact]
    public async Task InsufficientStock_DoesNotThrowAndEmitsFailureEvent()
    {
        // Arrange — seed only 2 units, then request 5.
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ProductWithOnHandAsync(
            productId,
            onHand: 2,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-2),
            TestContext.Current.CancellationToken);

        // Act — InsufficientStock is a business-expected outcome: the application layer writes the
        // failure event to the outbox, so the SagaCommandHandlerBase wrapper does NOT throw.
        var act = () => Fixture.DispatchAsync<ReserveStockCommandKafkaHandler, AvroReserveStockCommand>(
            new AvroReserveStockCommand
            {
                OrderId = orderId,
                ProductId = productId,
                ReservationId = reservationId,
                Quantity = 5,
                RequestedAtUtc = UtcNow,
            });

        // Assert
        await act.Should().NotThrowAsync();

        var auditRow = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        var streamLength = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(e => e.StreamId == productId, TestContext.Current.CancellationToken);
        var outboxRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            auditRow.Should().BeNull();
            streamLength.Should().Be(2, "only the seeded Initialize + Receive");

            // The outbox row is the failure event the saga compensates on — not a reserved event.
            outboxRows.Should().ContainSingle(m => m.TopicName == "inventory.reservations")
                .Which.Type.Should().BeMessageType<Inventory.Reservations.StockReservationFailedEvent>();
        }
    }
}
