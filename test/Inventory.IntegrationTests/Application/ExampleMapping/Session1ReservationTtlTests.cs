using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.ConfirmReservation;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReleaseReservation;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Application.ExampleMapping;

/// <summary>
/// M9 acceptance for the gap scenarios in
/// <c>docs/bc-design/example-mapping/inventory.md</c> § Session 1
/// (Reservation TTL auto-release).
/// </summary>
/// <remarks>
/// <para>
/// Session 1 examples already covered prior to M9:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>1.1 (saga confirms before expiry)</b> →
/// <c>ReservationExpiryWorkerTests.ConfirmedReservation_NotReleasedAfterExpiry</c>
/// — confirm at T0+2m, worker tick at T0+20m, no phantom release.
/// </description></item>
/// <item><description>
/// <b>1.2 (buyer abandons, TTL fires)</b> →
/// <c>ReservationExpiryWorkerTests.SingleExpiredReservation_IsReleasedWithExpiryReason</c>
/// — single tick at T0+16m, release reason = Expiry, external event queued.
/// </description></item>
/// </list>
/// <para>
/// This file fills the M9 gaps: <b>1.3</b> (confirm arriving after the reservation
/// has already been released by the worker) and <b>1.4</b> (a duplicate release
/// command after a previous Expiry release — no second event, no second outbox
/// row).
/// </para>
/// </remarks>
[Collection(nameof(IntegrationTestCollection))]
public sealed class Session1ReservationTtlTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset SeedUtc =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(15);

    public Session1ReservationTtlTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>
    /// Example 1.3 of <c>docs/bc-design/example-mapping/inventory.md</c>:
    /// reservation R3 was released with <c>ReleaseReason=Expiry</c> at T0+16m;
    /// the saga's confirm command arrives at T0+17m (delayed by retries).
    /// Verify R4: aggregate sees <c>Status=Released</c>, returns
    /// <c>Result.Fail(ReservationNotActiveError)</c>, no
    /// <c>ReservationConfirmedEvent</c> appended, no external event published.
    /// </summary>
    [Fact]
    public async Task Example1_3_ConfirmAfterExpiryRelease_FailsWithReservationNotActive()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await SeedActiveReservationAsync(productId, reservationId, orderId, quantity: 2);

        // Simulate the worker's release: write a real ReservationReleasedEvent
        // with reason=Expiry to the stream via the production handler. After
        // this, the aggregate's Reservations[R3].Status == Released.
        using (var releaseScope = Fixture.CreateScope())
        {
            var releaseHandler = releaseScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReleaseReservationCommand>>();
            var releaseResult = await releaseHandler.HandleAsync(
                new ReleaseReservationCommand
                {
                    ReservationId = reservationId,
                    ProductId = productId,
                    Reason = ReleaseReason.Expiry,
                    OccurredOnUtc = SeedUtc.AddMinutes(16),
                },
                TestContext.Current.CancellationToken);
            releaseResult.Should().BeSuccess();
        }

        // Now the saga's delayed Confirm arrives.
        using var scope = Fixture.CreateScope();
        var confirmHandler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ConfirmReservationCommand>>();

        var result = await confirmHandler.HandleAsync(
            new ConfirmReservationCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                OccurredOnUtc = SeedUtc.AddMinutes(17),
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ReservationNotActiveError>()
            .Which.Metadata["ErrorCode"].Should().Be("Inventory.ReservationNotActive");

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // Stream contains exactly the four expected events: Initialize +
        // Receive + Reserve + ReservationReleased(Expiry). NO Confirmed event.
        var eventTypes = await db.StockEvents
            .AsNoTracking()
            .Where(e => e.StreamId == productId)
            .OrderBy(e => e.Version)
            .Select(e => e.EventType)
            .ToListAsync(TestContext.Current.CancellationToken);
        eventTypes.Should().Equal(
            nameof(StockItemInitializedEvent),
            nameof(StockReceivedEvent),
            nameof(StockReservedEvent),
            nameof(ReservationReleasedEvent));

        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Released);
        audit.ReleaseReason.Should().Be(ReleaseReason.Expiry);

        // Outbox holds the original Reserve event + the Released(Expiry)
        // event. NO ReservationConfirmedEvent — the failed Confirm published
        // nothing.
        var outboxTypes = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.TopicName == "inventory.reservations")
            .OrderBy(m => m.CreatedUtc)
            .Select(m => m.Type)
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxTypes.Should().Equal(
            "Inventory.Reservations.StockReservedEvent",
            "Inventory.Reservations.ReservationReleasedEvent");
    }

    /// <summary>
    /// Example 1.4 of <c>docs/bc-design/example-mapping/inventory.md</c>:
    /// reservation R4 already released with <c>ReleaseReason=Expiry</c> at
    /// T0+16m; a duplicate release command (worker retry after crash, or saga
    /// retry) arrives at T0+17m. Verify R5: handler observes
    /// <c>R4.Status=Released</c> and treats the duplicate as a no-op — no new
    /// event appended, no new external event published.
    /// </summary>
    /// <remarks>
    /// The aggregate's <c>ReleaseReservation</c> branches on
    /// <c>Status=Released</c> by returning <c>Result.Ok</c> without raising a
    /// new event (per <c>StockItem.cs:248-252</c>). The application handler
    /// returns success up the stack. The acceptance is that the stream and the
    /// outbox each grew by exactly one row across the two release calls — not
    /// two.
    /// </remarks>
    [Fact]
    public async Task Example1_4_DuplicateReleaseExpiryCommand_IsNoOpWithNoSecondEvent()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await SeedActiveReservationAsync(productId, reservationId, orderId, quantity: 1);

        // First release: fires the real ReservationReleasedEvent + outbox row.
        using (var firstScope = Fixture.CreateScope())
        {
            var releaseHandler = firstScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReleaseReservationCommand>>();
            var firstResult = await releaseHandler.HandleAsync(
                new ReleaseReservationCommand
                {
                    ReservationId = reservationId,
                    ProductId = productId,
                    Reason = ReleaseReason.Expiry,
                    OccurredOnUtc = SeedUtc.AddMinutes(16),
                },
                TestContext.Current.CancellationToken);
            firstResult.Should().BeSuccess();
        }

        // Snapshot stream + outbox row counts after the first release so the
        // second release's no-op behaviour is asserted as "no growth".
        var releasedEventCountAfterFirst = await CountReleasedEventsAsync(productId);
        releasedEventCountAfterFirst.Should().Be(1);
        var releasedOutboxCountAfterFirst = await CountReleasedOutboxRowsAsync(orderId);
        releasedOutboxCountAfterFirst.Should().Be(1);

        // Duplicate release with the same ReservationId + same reason. The
        // aggregate reducer hits the Released branch (StockItem.cs:248-252)
        // and returns Result.Ok with no event raised; the EventStoreRepository
        // sees zero domain events to persist and returns Ok without touching
        // the DB (EventStoreRepository.cs:153-159).
        using (var secondScope = Fixture.CreateScope())
        {
            var releaseHandler = secondScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReleaseReservationCommand>>();
            var secondResult = await releaseHandler.HandleAsync(
                new ReleaseReservationCommand
                {
                    ReservationId = reservationId,
                    ProductId = productId,
                    Reason = ReleaseReason.Expiry,
                    OccurredOnUtc = SeedUtc.AddMinutes(17),
                },
                TestContext.Current.CancellationToken);
            secondResult.Should().BeSuccess(
                "duplicate Release on an already-Released reservation is an idempotent no-op per Session 1.R5");
        }

        // No second event appended.
        var releasedEventCountAfterSecond = await CountReleasedEventsAsync(productId);
        releasedEventCountAfterSecond.Should().Be(1, "the duplicate release must NOT append a second ReservationReleasedEvent");

        // No second external event queued on the outbox.
        var releasedOutboxCountAfterSecond = await CountReleasedOutboxRowsAsync(orderId);
        releasedOutboxCountAfterSecond.Should().Be(1, "the duplicate release must NOT enqueue a second external ReservationReleasedEvent");
    }

    private async Task SeedActiveReservationAsync(Guid productId, Guid reservationId, Guid orderId, int quantity)
    {
        using var seedScope = Fixture.CreateScope();
        var initHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
        var receiveHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();
        var reserveHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<ReserveStockCommand>>();

        (await initHandler.HandleAsync(
            new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = SeedUtc.AddMinutes(-2) },
            TestContext.Current.CancellationToken)).Should().BeSuccess();

        (await receiveHandler.HandleAsync(
            new ReceiveStockCommand
            {
                ProductId = productId,
                Quantity = Math.Max(quantity, 1),
                Source = "receiving-dock",
                ReceivedByUserId = null,
                OccurredOnUtc = SeedUtc.AddMinutes(-1),
            },
            TestContext.Current.CancellationToken)).Should().BeSuccess();

        (await reserveHandler.HandleAsync(
            new ReserveStockCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                Quantity = quantity,
                OrderId = orderId,
                TimeToLive = ReservationTtl,
                OccurredOnUtc = SeedUtc,
            },
            TestContext.Current.CancellationToken)).Should().BeSuccess();
    }

    private async Task<int> CountReleasedEventsAsync(Guid productId)
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(ReservationReleasedEvent),
                TestContext.Current.CancellationToken);
    }

    private async Task<int> CountReleasedOutboxRowsAsync(Guid orderId)
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.OutboxMessages
            .AsNoTracking()
            .CountAsync(
                m => m.KafkaKey == orderId.ToString()
                    && m.Type == "Inventory.Reservations.ReservationReleasedEvent",
                TestContext.Current.CancellationToken);
    }
}
