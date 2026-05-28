using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using AvroReleaseReason = Inventory.Reservations.ReleaseReason;
using AvroReleaseReservationCommand = Inventory.Reservations.ReleaseReservationCommand;

namespace Inventory.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Acceptance for <see cref="ReleaseReservationCommandKafkaHandler"/>.
/// Drives an Avro <see cref="AvroReleaseReservationCommand"/> through the
/// handler with <see cref="AvroReleaseReason.Compensation"/> (saga
/// rollback path) and asserts the audit row flips to Released, the
/// reserved quantity returns to Available, and the external
/// <c>ReservationReleasedEvent</c> lands in the outbox.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class ReleaseReservationCommandKafkaHandlerTests : BaseIntegrationTest
{
    private static readonly DateTime UtcNow =
        new(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);

    public ReleaseReservationCommandKafkaHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Compensation_AuditReleasedAndStockReturned()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 3,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        var avroCommand = new AvroReleaseReservationCommand
        {
            CorrelationId = correlationId,
            ProductId = productId,
            ReservationId = reservationId,
            ReleaseReason = AvroReleaseReason.Compensation,
            RequestedAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ReleaseReservationCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(context, avroCommand);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Released);
        audit.ReleaseReason.Should().Be(ReleaseReason.Compensation);
        audit.ResolvedAtUtc.Should().NotBeNull();

        var levels = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        // After Release: onHand=10 still (no decrement on release), reserved=0.
        levels.OnHand.Should().Be(10);
        levels.Reserved.Should().Be(0);
        levels.Available.Should().Be(10);

        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == "Inventory.Reservations.ReservationReleasedEvent")
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle()
            .Which.TopicName.Should().Be("inventory.reservations");
    }
}
