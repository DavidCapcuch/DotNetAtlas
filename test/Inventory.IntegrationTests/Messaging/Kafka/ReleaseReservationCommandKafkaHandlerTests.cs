using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Platform.Test.Framework.Assertions;
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

        await Seed.ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 3,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        await Fixture.DispatchAsync<ReleaseReservationCommandKafkaHandler, AvroReleaseReservationCommand>(
            new AvroReleaseReservationCommand
            {
                ProductId = productId,
                ReservationId = reservationId,
                ReleaseReason = AvroReleaseReason.Compensation,
                RequestedAtUtc = UtcNow,
            });

        var audit = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Released);
        audit.ReleaseReason.Should().Be(ReleaseReason.Compensation);
        audit.ResolvedAtUtc.Should().NotBeNull();

        var levels = await InventoryDbContext.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        // After Release: onHand=10 still (no decrement on release), reserved=0.
        levels.OnHand.Should().Be(10);
        levels.Reserved.Should().Be(0);
        levels.Available.Should().Be(10);

        var outboxRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == typeof(Inventory.Reservations.ReservationReleasedEvent).FullName)
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle()
            .Which.TopicName.Should().Be("inventory.reservations");
    }

    /// <summary>
    /// Example 1.4 of <c>docs/bc-design/example-mapping/inventory.md</c>: a duplicate expiry release
    /// (a worker retry after a crash, or a saga retry) on an already-Released reservation is a no-op —
    /// no second event on the stream, no second external event on the outbox.
    /// </summary>
    [Fact]
    [Trait("Category", "resilience")]
    public async Task WhenReleaseReplayed_IsNoOp_WithNoSecondEvent()
    {
        // Arrange — the first expiry release has already landed.
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 1,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken,
            onHand: 1);

        // Reserved at -3m with the default 15-min TTL, so +13m is past its expiry.
        var release = new AvroReleaseReservationCommand
        {
            ProductId = productId,
            ReservationId = reservationId,
            ReleaseReason = AvroReleaseReason.Expiry,
            RequestedAtUtc = UtcNow.AddMinutes(13),
        };
        await Fixture.DispatchAsync<ReleaseReservationCommandKafkaHandler, AvroReleaseReservationCommand>(release);

        // Act
        var replay = () => Fixture.DispatchAsync<ReleaseReservationCommandKafkaHandler, AvroReleaseReservationCommand>(release);

        // Assert
        await replay.Should().NotThrowAsync("a duplicate release on a Released reservation is an idempotent no-op");

        var releasedEvents = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(ReservationReleasedDomainEvent),
                TestContext.Current.CancellationToken);
        var outboxRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);
        var audit = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            releasedEvents.Should().Be(1, "the replay must not append a second ReservationReleasedDomainEvent");
            outboxRows.Should().ContainSingleMessageOfType<Inventory.Reservations.ReservationReleasedEvent>(
                because: "the replay must not enqueue a second external ReservationReleasedEvent");
            audit.ReleaseReason.Should().Be(ReleaseReason.Expiry, "the Avro reason maps onto the domain reason");
        }
    }
}
