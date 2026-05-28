using EntityFramework.Exceptions.PostgreSQL;
using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
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
/// <c>docs/bc-design/example-mapping/inventory.md</c> § Session 2
/// (Cannot oversell — Available = OnHand − Reserved).
/// </summary>
/// <remarks>
/// <para>
/// Session 2 examples already covered earlier:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>2.1 (sufficient stock)</b> →
/// <c>ReserveStockCommandHandlerTests.HappyPath_AppendsEventProjectionsAndOutboxAtomically</c>.
/// </description></item>
/// <item><description>
/// <b>2.2 (request exceeds Available)</b> →
/// <c>ReserveStockCommandHandlerTests.InsufficientStock_EmitsFailureEventAndAppendsNoStockEvent</c>.
/// </description></item>
/// </list>
/// <para>
/// This file fills the gaps: <b>2.3</b> (concurrent-reserve race resolved
/// by the <c>UNIQUE(StreamId, Version)</c> retry-then-fail path) and
/// <b>2.4</b> (rehydration is authoritative when a fresh receive lands right
/// before a reserve, regardless of projection lag).
/// </para>
/// <para>
/// 2.3 is asserted at the <see cref="EventStoreRepository"/> level using the
/// same <see cref="OneShotConflictInterceptor"/> precedent as
/// <c>EventStoreRepositoryTests.AppendAsync_ConcurrencyConflict_RetriesOnceAndSucceeds</c>:
/// the application-handler-side outbox emission of
/// <c>StockReservationFailedEvent</c> on the InsufficientStock branch is
/// exercised by <c>ReserveStockCommandHandlerTests</c> already; this test
/// proves the race-then-fail path through the repository, which is the new
/// behaviour Session 2.3 specifies (Verify R5).
/// </para>
/// </remarks>
[Collection<IntegrationTestCollection>]
public sealed class Session2CannotOversellTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    public Session2CannotOversellTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>
    /// Example 2.3 of <c>docs/bc-design/example-mapping/inventory.md</c>: two
    /// reserves race on the last 7 units. The "loser" rehydrates at V=2,
    /// computes Available=7≥5, and tries to append <c>StockReservedEvent</c>
    /// at V=3. Meanwhile the "winner" beat us to V=3 (simulated here via
    /// <see cref="OneShotConflictInterceptor"/> injecting a competing
    /// <c>StockReservedEvent(qty=5)</c>). Verify R5: the loser's
    /// <see cref="EventStoreRepository.AppendAsync"/> hits
    /// <c>UniqueConstraintException</c>, retries exactly once, re-rehydrates
    /// at V=3 (Available=2), discovers <c>2 &lt; 5</c>, and returns
    /// <c>Result.Fail(InsufficientStockError)</c> with the up-to-date
    /// <c>Available=2</c> in the error metadata.
    /// </summary>
    /// <remarks>
    /// Pattern mirrors
    /// <c>EventStoreRepositoryTests.AppendAsync_ConcurrencyConflict_RetriesOnceAndSucceeds</c>
    /// (precedent at <c>test/Inventory.IntegrationTests/Persistence/EventStoreRepositoryTests.cs:152</c>);
    /// the helper methods <c>CreateInterceptedDbContext</c> and
    /// <c>InsertCompetingRowAsync</c> are copied locally (rather than extracted
    /// to <c>Common/</c>) to keep this change inside its own file boundary —
    /// see plan §Out-of-scope. Application-level outbox emission of
    /// <c>StockReservationFailedEvent</c> on this fail path is covered by
    /// <c>ReserveStockCommandHandlerTests.InsufficientStock_EmitsFailureEventAndAppendsNoStockEvent</c>.
    /// </remarks>
    [Fact]
    public async Task Example2_3_ConcurrentReserveOnLastUnits_LoserRetriesThenFailsWithInsufficientStock()
    {
        var productId = Guid.NewGuid();
        var winningReservationId = Guid.NewGuid();
        var winningOrderId = Guid.NewGuid();
        var losingReservationId = Guid.NewGuid();
        var losingOrderId = Guid.NewGuid();

        // Arrange: stream at V=2 with OnHand=7, Reserved=0, Available=7.
        using (var setupScope = Fixture.CreateScope())
        {
            var setupRepo = setupScope.ServiceProvider.GetRequiredService<EventStoreRepository>();
            var init = await setupRepo.AppendAsync(
                productId,
                a => a.Initialize(productId, UtcNow.AddMinutes(-2)),
                correlationId: null,
                TestContext.Current.CancellationToken);
            init.Should().BeSuccess();

            var receive = await setupRepo.AppendAsync(
                productId,
                a => a.ReceiveStock(7, StockSource.ReceivingDock, null, UtcNow.AddMinutes(-1)),
                correlationId: null,
                TestContext.Current.CancellationToken);
            receive.Should().BeSuccess();
        }

        // Build the competing event: the "winner" reserves 5 units at V=3.
        var winningReservedEvent = new StockReservedEvent
        {
            ProductId = productId,
            ReservationId = winningReservationId,
            Quantity = 5,
            OrderId = winningOrderId,
            ExpiresAtUtc = UtcNow.AddMinutes(15),
            OccurredOnUtc = UtcNow,
        };

        var interceptor = new OneShotConflictInterceptor(
            ct => InsertCompetingRowAsync(productId, version: 3, @event: winningReservedEvent, ct),
            fireCount: 1);

        await using var raceCtx = CreateInterceptedDbContext(interceptor);
        var raceRepo = new EventStoreRepository(raceCtx, NoOpDomainEventDispatcher.Instance);

        // Act: the "loser" attempts to reserve 5 units. First attempt collides
        // at V=3, the catch clause clears the ChangeTracker, re-rehydrates at
        // V=3 (now Available=2), and the aggregate's Reserve method returns
        // InsufficientStockError without raising any new event.
        var loserResult = await raceRepo.AppendAsync(
            productId,
            a => a.Reserve(
                ReservationId.Create(losingReservationId).Value,
                quantity: 5,
                losingOrderId,
                TimeSpan.FromMinutes(15),
                UtcNow).ToResult(),
            correlationId: null,
            TestContext.Current.CancellationToken);

        loserResult.Should().BeFailure();
        loserResult.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<InsufficientStockError>()
            .Which.Available.Should().Be(2,
                "after the winner appended at V=3, the loser's retry rehydrates the up-to-date Available");

        // Verify: the stream contains exactly Init + Receive + Winning Reserve
        // — no losing reserve row, no extra event from the failed attempt.
        using var verifyScope = Fixture.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var rows = await verifyCtx.StockEvents
            .AsNoTracking()
            .Where(r => r.StreamId == productId)
            .OrderBy(r => r.Version)
            .Select(r => new { r.Version, r.EventType })
            .ToListAsync(TestContext.Current.CancellationToken);

        rows.Should().HaveCount(3);
        rows[0].EventType.Should().Be(nameof(StockItemInitializedEvent));
        rows[1].EventType.Should().Be(nameof(StockReceivedEvent));
        rows[2].EventType.Should().Be(nameof(StockReservedEvent));
        rows[2].Version.Should().Be(3);
    }

    /// <summary>
    /// Example 2.4 of <c>docs/bc-design/example-mapping/inventory.md</c>:
    /// the stream is empty (V=1 after Initialize), admin issues
    /// <c>ReceiveStockCommand(qty=10)</c>, then the saga immediately issues
    /// <c>ReserveStockCommand(qty=5)</c>. Verify R6: the reserve handler
    /// rehydrates the stream (sees the freshly-appended Receive event at V=2,
    /// OnHand=10), evaluates <c>Available = 10 ≥ 5</c>, and appends
    /// <c>StockReservedEvent</c> at V=3 — proving rehydration is the
    /// authoritative read path, not the projection table (which may lag).
    /// </summary>
    /// <remarks>
    /// In Inventory's design the projection writes happen inside the same
    /// transaction as the event append (see <c>EventStoreRepository.cs:175-188</c>),
    /// so projection lag is not a real-world concern in v1. This test still
    /// proves the design intent: even if projections were eventually
    /// consistent, rehydration is the source of truth and the back-to-back
    /// receive→reserve flow always succeeds when the math allows.
    /// </remarks>
    [Fact]
    public async Task Example2_4_FreshReceiptUnlocksImmediateReserveViaRehydration()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Empty stream at V=1.
        using (var initScope = Fixture.CreateScope())
        {
            var initHandler = initScope.ServiceProvider
                .GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
            (await initHandler.HandleAsync(
                new InitializeStockItemCommand
                {
                    ProductId = productId,
                    OccurredOnUtc = UtcNow.AddMinutes(-2),
                },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        // Admin receive: stream at V=2 with OnHand=10.
        using (var receiveScope = Fixture.CreateScope())
        {
            var receiveHandler = receiveScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();
            (await receiveHandler.HandleAsync(
                new ReceiveStockCommand
                {
                    ProductId = productId,
                    Quantity = 10,
                    Source = "receiving-dock",
                    ReceivedByUserId = null,
                    OccurredOnUtc = UtcNow.AddMinutes(-1),
                },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        // Saga reserves 5 immediately. The fresh DI scope below has its own
        // EventStoreRepository which calls RehydrateAsync — the Receive event
        // at V=2 must be visible there for Available=10 evaluation to allow
        // the reserve.
        using var reserveScope = Fixture.CreateScope();
        var reserveHandler = reserveScope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReserveStockCommand>>();
        var reserveResult = await reserveHandler.HandleAsync(
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

        reserveResult.Should().BeSuccess();

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // Stream now has Init + Receive + Reserve at V=3.
        var rows = await db.StockEvents
            .AsNoTracking()
            .Where(e => e.StreamId == productId)
            .OrderBy(e => e.Version)
            .Select(e => new { e.Version, e.EventType })
            .ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().HaveCount(3);
        rows[2].Version.Should().Be(3);
        rows[2].EventType.Should().Be("StockReservedEvent");

        var levels = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        levels.OnHand.Should().Be(10);
        levels.Reserved.Should().Be(5);
        levels.Available.Should().Be(5);
    }

    // ---- helpers (precedent: EventStoreRepositoryTests.cs:318, 331) ----

    private InventoryDbContext CreateInterceptedDbContext(OneShotConflictInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(Fixture.ConnectionString, npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .UseExceptionProcessor()
            .AddInterceptors(interceptor)
            .Options;

        return new InventoryDbContext(options);
    }

    private async Task InsertCompetingRowAsync(
        Guid streamId,
        int version,
        DomainEvent @event,
        CancellationToken ct)
    {
        using var scope = Fixture.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var (eventType, payload) = StockEventSerializer.Serialize(@event);
        var row = StockEventRow.Create(
            streamId: streamId,
            version: version,
            eventType: eventType,
            payload: payload,
            occurredAtUtc: @event.OccurredOnUtc,
            correlationId: null);

        ctx.StockEvents.Add(row);
        await ctx.SaveChangesAsync(ct);
    }
}
