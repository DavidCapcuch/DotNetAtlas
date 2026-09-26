using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Platform.SharedKernel.Exceptions;
using Platform.Test.Framework.Assertions;
using AvroConfirmReservationCommand = Inventory.Reservations.ConfirmReservationCommand;

namespace Inventory.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Acceptance for <see cref="ConfirmReservationCommandKafkaHandler"/>.
/// Drives an Avro <see cref="AvroConfirmReservationCommand"/> through the
/// handler and asserts the audit row flips to <c>Confirmed</c>, OnHand
/// decrements, and the external <c>ReservationConfirmedEvent</c> lands in
/// the outbox.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class ConfirmReservationCommandKafkaHandlerTests : BaseIntegrationTest
{
    private static readonly DateTime UtcNow =
        new(2026, 4, 25, 11, 0, 0, DateTimeKind.Utc);

    public ConfirmReservationCommandKafkaHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task HappyPath_AuditConfirmedAndOutboxEmitted()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 4,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        // Act
        await Fixture.DispatchAsync<ConfirmReservationCommandKafkaHandler, AvroConfirmReservationCommand>(
            new AvroConfirmReservationCommand
            {
                ProductId = productId,
                ReservationId = reservationId,
                RequestedAtUtc = UtcNow,
            });

        // Assert
        var audit = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        var levels = await InventoryDbContext.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        var outboxRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            audit.Status.Should().Be(ReservationStatus.Confirmed);
            audit.ResolvedAtUtc.Should().NotBeNull();

            // Seeded with onHand=10, reserved=4 -> after Confirm: onHand=6, reserved=0.
            levels.OnHand.Should().Be(6);
            levels.Reserved.Should().Be(0);
            levels.Available.Should().Be(6);

            // The order's outbox holds exactly the seeded reserve plus this confirm: Confirm emits one
            // event and nothing else.
            outboxRows.Should().HaveCount(2)
                .And.OnlyContain(m => m.TopicName == "inventory.reservations");
            outboxRows.Should().ContainSingleMessageOfType<Inventory.Reservations.StockReservedEvent>();
            outboxRows.Should().ContainSingleMessageOfType<Inventory.Reservations.ReservationConfirmedEvent>();
        }
    }

    /// <summary>
    /// Example 3.2 of <c>docs/bc-design/example-mapping/inventory.md</c>: a replayed confirm (saga retry)
    /// on an already-Confirmed reservation is a no-op. The typed handler is driven directly, so the
    /// replay bypasses the inbox dedup and reaches the aggregate — it is the aggregate's idempotence
    /// under test here.
    /// </summary>
    [Fact]
    [Trait("Category", "resilience")]
    public async Task WhenConfirmReplayed_IsNoOp_WithNoSecondEventAndProjectionUnchanged()
    {
        // Arrange — one confirm has already landed.
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

        var confirm = new AvroConfirmReservationCommand
        {
            ProductId = productId,
            ReservationId = reservationId,
            RequestedAtUtc = UtcNow,
        };
        await Fixture.DispatchAsync<ConfirmReservationCommandKafkaHandler, AvroConfirmReservationCommand>(confirm);

        // Act
        var replay = () => Fixture.DispatchAsync<ConfirmReservationCommandKafkaHandler, AvroConfirmReservationCommand>(confirm);

        // Assert
        await replay.Should().NotThrowAsync("a duplicate confirm on a Confirmed reservation is an idempotent no-op");

        var confirmedEvents = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(ReservationConfirmedDomainEvent),
                TestContext.Current.CancellationToken);
        var outboxRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);
        var levels = await InventoryDbContext.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            confirmedEvents.Should().Be(1, "the replay must not append a second ReservationConfirmedDomainEvent");
            outboxRows.Should().ContainSingleMessageOfType<Inventory.Reservations.ReservationConfirmedEvent>(
                because: "the replay must not enqueue a second external ReservationConfirmedEvent");
            levels.OnHand.Should().Be(7, "10 on hand - 3 confirmed, decremented once");
            levels.Reserved.Should().Be(0);
            levels.Available.Should().Be(7);
        }
    }

    // Refused whatever released it: Examples 1.3 and 3.3 of docs/bc-design/example-mapping/inventory.md.
    [Theory]
    [Trait("Category", "resilience")]
    [InlineData(ReleaseReason.Cancellation)]
    [InlineData(ReleaseReason.Compensation)]
    [InlineData(ReleaseReason.Expiry)]
    public async Task WhenReservationAlreadyReleased_ThrowsSagaCommandDispatchException(ReleaseReason releaseReason)
    {
        // ReservationNotActive is NOT in SagaCommandHandlerBase.BusinessExpectedErrorCodes
        // (only "Inventory.InsufficientStock" is allowlisted), so a Confirm against
        // an already-Released reservation must throw SagaCommandDispatchException —
        // KafkaFlow then routes the message to the command-topic DLT for operator
        // triage. Without this test, a regression that added the code to the
        // allowlist would silently break saga semantics (the staged inbox row
        // would commit but no ReservationConfirmedEvent would land in the outbox
        // and the saga would stall waiting for either response).

        // Arrange
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ReleasedReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 4,
            releaseReason,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        // Act
        var act = () => Fixture.DispatchAsync<ConfirmReservationCommandKafkaHandler, AvroConfirmReservationCommand>(
            new AvroConfirmReservationCommand
            {
                ProductId = productId,
                ReservationId = reservationId,
                RequestedAtUtc = UtcNow,
            });

        // Assert — refused for the reservation's terminal status, not for any other non-allowlisted failure.
        await act.Should().ThrowAsync<SagaCommandDispatchException>()
            .WithMessage("*is not Active (current status: Released)*");

        var confirmedRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == typeof(Inventory.Reservations.ReservationConfirmedEvent).FullName)
            .ToListAsync(TestContext.Current.CancellationToken);
        var confirmedEvents = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(ReservationConfirmedDomainEvent),
                TestContext.Current.CancellationToken);
        var audit = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            confirmedRows.Should().BeEmpty(
                "the wrapper threw before the staged outbox row could commit");
            confirmedEvents.Should().Be(0);
            audit.Status.Should().Be(ReservationStatus.Released);
            audit.ReleaseReason.Should().Be(releaseReason);
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task WhenReservationIdUnknown_ThrowsDataIntegrityException()
    {
        // The aggregate raises DataIntegrityException("Inventory.ReservationUnknown",
        // ...) when Confirm targets a ReservationId that was never reserved on
        // the stream. This is bug-class, not business-expected — there is no
        // allowlist entry for it, and the unhandled exception propagates through
        // the wrapper (rolling back the tx) so KafkaFlow's DLT middleware
        // routes the message for operator inspection.
        var productId = Guid.NewGuid();
        var unknownReservationId = Guid.NewGuid();

        await Seed.ProductWithOnHandAsync(
            productId,
            onHand: 10,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        var act = () => Fixture.DispatchAsync<ConfirmReservationCommandKafkaHandler, AvroConfirmReservationCommand>(
            new AvroConfirmReservationCommand
            {
                ProductId = productId,
                ReservationId = unknownReservationId,
                RequestedAtUtc = UtcNow,
            });
        await act.Should().ThrowAsync<DataIntegrityException>()
            .Where(e => e.Message.Contains(unknownReservationId.ToString()));

        var auditRow = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationId == unknownReservationId, TestContext.Current.CancellationToken);
        auditRow.Should().BeNull("no reservation existed for this id; the aggregate rejected before any persistence");
    }
}
