using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReleaseReservation;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Platform.SharedKernel.Exceptions;
using Platform.Test.Framework.Kafka;
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
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 4,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        var avroCommand = new AvroConfirmReservationCommand
        {
            CorrelationId = correlationId,
            ProductId = productId,
            ReservationId = reservationId,
            RequestedAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ConfirmReservationCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(context, avroCommand);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Confirmed);
        audit.ResolvedAtUtc.Should().NotBeNull();

        var levels = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        // Seeded with onHand=10, reserved=4 -> after Confirm: onHand=6, reserved=0.
        levels.OnHand.Should().Be(6);
        levels.Reserved.Should().Be(0);

        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == typeof(Inventory.Reservations.ReservationConfirmedEvent).FullName)
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle()
            .Which.TopicName.Should().Be("inventory.reservations");
    }

    [Fact]
    public async Task WhenReservationAlreadyReleased_ThrowsSagaCommandDispatchException()
    {
        // ReservationNotActive is NOT in SagaCommandHandlerBase.BusinessExpectedErrorCodes
        // (only "Inventory.InsufficientStock" is allowlisted), so a Confirm against
        // an already-Released reservation must throw SagaCommandDispatchException —
        // KafkaFlow then routes the message to the command-topic DLT for operator
        // triage. Without this test, a regression that added the code to the
        // allowlist would silently break saga semantics (the staged inbox row
        // would commit but no ReservationConfirmedEvent would land in the outbox
        // and the saga would stall waiting for either response).
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ReleasedReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 4,
            ReleaseReason.Cancellation,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        var avroCommand = new AvroConfirmReservationCommand
        {
            CorrelationId = Guid.NewGuid(),
            ProductId = productId,
            ReservationId = reservationId,
            RequestedAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ConfirmReservationCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        var act = async () => await handler.Handle(context, avroCommand);
        await act.Should().ThrowAsync<SagaCommandDispatchException>();

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var confirmedRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == typeof(Inventory.Reservations.ReservationConfirmedEvent).FullName)
            .ToListAsync(TestContext.Current.CancellationToken);
        confirmedRows.Should().BeEmpty(
            "the wrapper threw before the staged outbox row could commit");
    }

    [Fact]
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

        var avroCommand = new AvroConfirmReservationCommand
        {
            CorrelationId = Guid.NewGuid(),
            ProductId = productId,
            ReservationId = unknownReservationId,
            RequestedAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ConfirmReservationCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        var act = async () => await handler.Handle(context, avroCommand);
        await act.Should().ThrowAsync<DataIntegrityException>()
            .Where(e => e.Message.Contains(unknownReservationId.ToString()));

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var auditRow = await db.ReservationAudit
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationId == unknownReservationId, TestContext.Current.CancellationToken);
        auditRow.Should().BeNull("no reservation existed for this id; the aggregate rejected before any persistence");
    }
}
