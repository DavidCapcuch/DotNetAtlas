using EntityFramework.Exceptions.PostgreSQL;
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
using Inventory.Infrastructure.Persistence.EventStore;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.IntegrationTests.Application.ExampleMapping;

/// <summary>
/// Acceptance for the gap scenarios in
/// <c>docs/bc-design/example-mapping/inventory.md</c> § Session 3
/// (Confirm decrements OnHand by Quantity — idempotent).
/// </summary>
/// <remarks>
/// <para>
/// Session 3 examples already covered earlier:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>3.1 (confirm commits)</b> →
/// <c>ConfirmReservationCommandHandlerTests.TransitionsAuditAndEmitsExternalEventAndDecrementsStock</c>
/// — confirm transitions audit, decrements OnHand, emits external event.
/// </description></item>
/// </list>
/// <para>
/// This file fills the gaps: <b>3.2</b> (replayed confirm is a no-op),
/// <b>3.3</b> (confirm on a Released reservation fails with
/// <c>ReservationNotActive</c>), and <b>3.4</b> (confirm-vs-expiry version
/// race resolved by the <c>UNIQUE(StreamId, Version)</c> retry path).
/// </para>
/// <para>
/// 3.4 is asserted at the <see cref="EventStoreRepository"/> level using the
/// same <see cref="OneShotConflictInterceptor"/> precedent as
/// <c>EventStoreRepositoryTests.AppendAsync_ConcurrencyConflict_RetriesOnceAndSucceeds</c>:
/// the application-handler outbox emission for the success path of Confirm
/// is exercised by <c>ConfirmReservationCommandHandlerTests</c> already; this
/// test proves the race-then-fail path through the repository, which is the
/// new behaviour Session 3.4 specifies (Verify R5).
/// </para>
/// </remarks>
[Collection<IntegrationTestCollection>]
public sealed class Session3ConfirmIdempotencyTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 5, 1, 11, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(15);

    public Session3ConfirmIdempotencyTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>
    /// Example 3.2 of <c>docs/bc-design/example-mapping/inventory.md</c>:
    /// reservation R1 was already confirmed in the previous saga tick; a
    /// duplicate confirm command arrives. Verify R3: handler observes
    /// <c>Status != Active</c>, treats the command as a no-op — no event
    /// appended, no external event published, OnHand/Reserved/Available
    /// unchanged from the first confirm.
    /// </summary>
    [Fact]
    public async Task Example3_2_ReplayedConfirm_NoSecondEventAndProjectionUnchanged()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 3,
            UtcNow.AddMinutes(-3),
            TestContext.Current.CancellationToken,
            onHand: 10,
            timeToLive: ReservationTtl);

        // First confirm: real ReservationConfirmedDomainEvent + outbox row.
        using (var firstScope = Fixture.CreateScope())
        {
            var confirmHandler = firstScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ConfirmReservationCommand>>();
            var firstResult = await confirmHandler.HandleAsync(
                new ConfirmReservationCommand
                {
                    ReservationId = reservationId,
                    ProductId = productId,
                    OccurredOnUtc = UtcNow,
                },
                TestContext.Current.CancellationToken);
            firstResult.Should().BeSuccess();
        }

        // Snapshot post-first-confirm state.
        var confirmedEventCountAfterFirst = await CountConfirmedEventsAsync(productId);
        confirmedEventCountAfterFirst.Should().Be(1);
        var confirmedOutboxCountAfterFirst = await CountConfirmedOutboxRowsAsync(orderId);
        confirmedOutboxCountAfterFirst.Should().Be(1);

        var (onHandAfterFirst, reservedAfterFirst, availableAfterFirst) = await ReadProjectionAsync(productId);
        onHandAfterFirst.Should().Be(7); // 10 - 3
        reservedAfterFirst.Should().Be(0);
        availableAfterFirst.Should().Be(7);

        // Replay: aggregate's ConfirmReservation hits the Confirmed branch
        // (StockItem.cs:199-201) and returns Result.Ok with no event raised;
        // the EventStoreRepository sees zero events to persist and short-
        // circuits without touching the DB (EventStoreRepository.cs:153-159).
        using (var secondScope = Fixture.CreateScope())
        {
            var confirmHandler = secondScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ConfirmReservationCommand>>();
            var secondResult = await confirmHandler.HandleAsync(
                new ConfirmReservationCommand
                {
                    ReservationId = reservationId,
                    ProductId = productId,
                    OccurredOnUtc = UtcNow.AddMinutes(1),
                },
                TestContext.Current.CancellationToken);
            secondResult.Should().BeSuccess(
                "duplicate Confirm on an already-Confirmed reservation is an idempotent no-op per Session 3.R3");
        }

        // No second event, no second outbox row, projection unchanged.
        var confirmedEventCountAfterSecond = await CountConfirmedEventsAsync(productId);
        confirmedEventCountAfterSecond.Should().Be(1, "the duplicate Confirm must NOT append a second ReservationConfirmedDomainEvent");

        var confirmedOutboxCountAfterSecond = await CountConfirmedOutboxRowsAsync(orderId);
        confirmedOutboxCountAfterSecond.Should().Be(1, "the duplicate Confirm must NOT enqueue a second external ReservationConfirmedEvent");

        var (onHandAfterSecond, reservedAfterSecond, availableAfterSecond) = await ReadProjectionAsync(productId);
        onHandAfterSecond.Should().Be(onHandAfterFirst);
        reservedAfterSecond.Should().Be(reservedAfterFirst);
        availableAfterSecond.Should().Be(availableAfterFirst);
    }

    /// <summary>
    /// Example 3.3 of <c>docs/bc-design/example-mapping/inventory.md</c>:
    /// reservation R2 was released earlier (e.g. saga compensation); a stray
    /// confirm command arrives out-of-order. Verify R4: handler returns
    /// <c>Result.Fail(ReservationNotActiveError)</c>, no event appended, no
    /// external event published.
    /// </summary>
    [Fact]
    public async Task Example3_3_ConfirmOnReleasedReservation_FailsWithReservationNotActive()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 2,
            UtcNow.AddMinutes(-3),
            TestContext.Current.CancellationToken,
            onHand: 5,
            timeToLive: ReservationTtl);

        // Release with reason=Compensation (saga compensation, not TTL).
        using (var releaseScope = Fixture.CreateScope())
        {
            var releaseHandler = releaseScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReleaseReservationCommand>>();
            (await releaseHandler.HandleAsync(
                new ReleaseReservationCommand
                {
                    ReservationId = reservationId,
                    ProductId = productId,
                    Reason = ReleaseReason.Compensation,
                    OccurredOnUtc = UtcNow.AddMinutes(-1),
                },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        // Stray confirm.
        using var confirmScope = Fixture.CreateScope();
        var confirmHandler = confirmScope.ServiceProvider
            .GetRequiredService<ICommandHandler<ConfirmReservationCommand>>();

        var result = await confirmHandler.HandleAsync(
            new ConfirmReservationCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                OccurredOnUtc = UtcNow,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ReservationNotActiveError>()
            .Which.ErrorCode.Should().Be("Inventory.ReservationNotActive");

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // No ReservationConfirmedDomainEvent on the stream.
        var confirmedCount = await db.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(ReservationConfirmedDomainEvent),
                TestContext.Current.CancellationToken);
        confirmedCount.Should().Be(0);

        // No external ReservationConfirmedEvent in the outbox.
        var confirmedOutboxCount = await db.OutboxMessages
            .AsNoTracking()
            .CountAsync(
                m => m.KafkaKey == orderId.ToString()
                    && m.Type == typeof(Inventory.Reservations.ReservationConfirmedEvent).FullName,
                TestContext.Current.CancellationToken);
        confirmedOutboxCount.Should().Be(0);

        // Audit row stays Released/Compensation.
        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Released);
        audit.ReleaseReason.Should().Be(ReleaseReason.Compensation);
    }

    /// <summary>
    /// Example 3.4 of <c>docs/bc-design/example-mapping/inventory.md</c>:
    /// reservation R3 at V=3, both <c>ReservationExpiryWorker</c> and saga's
    /// confirm dispatch within the same instant. Both rehydrate at V=3 and
    /// each tries to append at V=4. Verify R5: exactly one INSERT at V=4
    /// succeeds (here the simulated competing release wins via the
    /// interceptor); the loser (Confirm) hits
    /// <c>UniqueConstraintException</c>, retries, observes
    /// <c>R3.Status=Released</c>, and returns
    /// <c>Result.Fail(ReservationNotActiveError)</c>. Stream contains exactly
    /// one resolution event for R3.
    /// </summary>
    /// <remarks>
    /// Tested at the <see cref="EventStoreRepository"/> level — same precedent
    /// as Session 2.3. Intercepted-DbContext + competing-row insert helpers
    /// live on <see cref="EventStoreTestExtensions"/>.
    /// </remarks>
    [Fact]
    public async Task Example3_4_ConfirmVsExpiryRace_LoserObservesTerminalAndFails()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Arrange: stream at V=3 with an Active reservation.
        using (var setupScope = Fixture.CreateScope())
        {
            var setupRepo = setupScope.ServiceProvider.GetRequiredService<EventStoreRepository>();
            (await setupRepo.AppendAsync(
                productId,
                a => a.Initialize(productId, UtcNow.AddMinutes(-3)),
                correlationId: null,
                TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await setupRepo.AppendAsync(
                productId,
                a => a.ReceiveStock(5, StockSource.ReceivingDock, null, UtcNow.AddMinutes(-2)),
                correlationId: null,
                TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await setupRepo.AppendAsync(
                productId,
                a => a.Reserve(
                    ReservationId.Create(reservationId).Value,
                    quantity: 2,
                    orderId,
                    ReservationTtl,
                    UtcNow.AddMinutes(-1)).ToResult(),
                correlationId: null,
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        // Build the competing event: the expiry worker wins and appends
        // ReservationReleasedDomainEvent(Expiry) at V=4.
        var competingReleasedEvent = new ReservationReleasedDomainEvent
        {
            ProductId = productId,
            ReservationId = reservationId,
            ReleaseReason = ReleaseReason.Expiry,
            ReleasedAtUtc = UtcNow,
            OccurredOnUtc = UtcNow,
        };

        var interceptor = new OneShotConflictInterceptor(
            ct => Fixture.InsertEventStoreRowAsync(productId, version: 4, @event: competingReleasedEvent, ct),
            fireCount: 1);

        await using var raceCtx = Fixture.CreateInterceptedDbContext(interceptor);
        var raceRepo = new EventStoreRepository(raceCtx, NoOpDomainEventDispatcher.Instance);

        // Act: the loser (Confirm) attempts to append ReservationConfirmedDomainEvent
        // at V=4. The interceptor injects the competing release row; the
        // SaveChanges hits UniqueConstraintException, ChangeTracker is
        // cleared, the next iteration rehydrates at V=4 and sees Status=
        // Released. ConfirmReservation hits the Released branch
        // (StockItem.cs:203-204) and returns
        // Fail(ReservationNotActiveError).
        var loserResult = await raceRepo.AppendAsync(
            productId,
            a => a.ConfirmReservation(ReservationId.Create(reservationId).Value, UtcNow),
            correlationId: null,
            TestContext.Current.CancellationToken);

        loserResult.Should().BeFailure();
        loserResult.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ReservationNotActiveError>()
            .Which.CurrentStatus.Should().Be(ReservationStatus.Released,
                "after the competing release wins at V=4, the retried Confirm rehydrates and sees the terminal Released status");

        // Verify: exactly four rows on the stream — Init, Receive, Reserve,
        // ReservationReleased. NO ReservationConfirmedDomainEvent at any version.
        using var verifyScope = Fixture.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var rows = await verifyCtx.StockEvents
            .AsNoTracking()
            .Where(r => r.StreamId == productId)
            .OrderBy(r => r.Version)
            .Select(r => new { r.Version, r.EventType })
            .ToListAsync(TestContext.Current.CancellationToken);

        rows.Should().HaveCount(4);
        rows[0].EventType.Should().BeEventType<StockItemInitializedDomainEvent>();
        rows[1].EventType.Should().BeEventType<StockReceivedDomainEvent>();
        rows[2].EventType.Should().BeEventType<StockReservedDomainEvent>();
        rows[3].Version.Should().Be(4);
        rows[3].EventType.Should().BeEventType<ReservationReleasedDomainEvent>(
            "exactly one resolution event for R3 — the competing release that won the version race");
    }

    private async Task<int> CountConfirmedEventsAsync(Guid productId)
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(ReservationConfirmedDomainEvent),
                TestContext.Current.CancellationToken);
    }

    private async Task<int> CountConfirmedOutboxRowsAsync(Guid orderId)
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.OutboxMessages
            .AsNoTracking()
            .CountAsync(
                m => m.KafkaKey == orderId.ToString()
                    && m.Type == typeof(Inventory.Reservations.ReservationConfirmedEvent).FullName,
                TestContext.Current.CancellationToken);
    }

    private async Task<(int OnHand, int Reserved, int Available)> ReadProjectionAsync(Guid productId)
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var row = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        return (row.OnHand, row.Reserved, row.Available);
    }
}
