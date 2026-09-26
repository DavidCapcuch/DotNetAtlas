using EntityFramework.Exceptions.PostgreSQL;
using FluentResults.Extensions.FluentAssertions;
using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.Infrastructure.Persistence.EventStore;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.IntegrationTests.Persistence;

/// <summary>
/// Integration tests for <see cref="EventStoreRepository"/> against a real
/// Testcontainers Postgres — the acceptance signal. Covers rehydration
/// on an empty stream, round-trip persistence, optimistic-concurrency
/// retry-once success, retry-exhausted failure with
/// <see cref="ConcurrencyError"/>, and command-delegate fast-fail semantics.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class EventStoreRepositoryTests : BaseIntegrationTest
{
    // Pinned so the jsonb payload assertions are bit-exact (ISO 8601).
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);

    public EventStoreRepositoryTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task RehydrateAsync_EmptyStream_ReturnsVersionZeroAggregate()
    {
        var productId = Guid.NewGuid();

        using var scope = Fixture.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<EventStoreRepository>();

        var aggregate = await repo.RehydrateAsync(productId, TestContext.Current.CancellationToken);

        aggregate.Version.Should().Be(0);
        aggregate.OnHand.Should().Be(0);
        aggregate.Reserved.Should().Be(0);
        aggregate.Reservations.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_InitializeThenReceive_PersistsTwoRowsAtVersions1And2()
    {
        var productId = Guid.NewGuid();

        using var scope = Fixture.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<EventStoreRepository>();

        var result = await repo.AppendAsync(
            productId,
            item =>
            {
                var init = item.Initialize(productId, UtcNow);
                if (init.IsFailed)
                {
                    return init;
                }

                return item.ReceiveStock(100, StockSource.ReceivingDock, null, UtcNow);
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Version.Should().Be(2);
        result.Value.OnHand.Should().Be(100);

        var rows = await ReadStreamAsync(productId);

        rows.Should().HaveCount(2);
        rows[0].Version.Should().Be(1);
        rows[0].EventType.Should().BeEventType<StockItemInitializedDomainEvent>();
        rows[1].Version.Should().Be(2);
        rows[1].EventType.Should().BeEventType<StockReceivedDomainEvent>();
    }

    [Fact]
    public async Task AppendAsync_ThenRehydrate_RoundTripsState()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using var scope = Fixture.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<EventStoreRepository>();

        var result = await repo.AppendAsync(
            productId,
            item =>
            {
                var steps = item.Initialize(productId, UtcNow);
                if (steps.IsFailed)
                {
                    return steps;
                }

                steps = item.ReceiveStock(100, StockSource.ReceivingDock, null, UtcNow);
                if (steps.IsFailed)
                {
                    return steps;
                }

                var reserve = item.Reserve(
                    ReservationId.Create(reservationId).Value,
                    quantity: 30,
                    orderId: orderId,
                    ttl: TimeSpan.FromMinutes(15),
                    occurredOnUtc: UtcNow);

                return reserve.ToResult();
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();

        using var rehydrateScope = Fixture.CreateScope();
        var rehydrateRepo = rehydrateScope.ServiceProvider.GetRequiredService<EventStoreRepository>();

        var rehydrated = await rehydrateRepo.RehydrateAsync(
            productId,
            TestContext.Current.CancellationToken);

        rehydrated.Version.Should().Be(3);
        rehydrated.OnHand.Should().Be(100);
        rehydrated.Reserved.Should().Be(30);
        rehydrated.Available.Should().Be(70);
        rehydrated.Reservations.Should().HaveCount(1);
        rehydrated.Reservations[ReservationId.Create(reservationId).Value].Quantity.Should().Be(30);
    }

    [Fact]
    [Trait("Category", "concurrency")]
    public async Task AppendAsync_ConcurrencyConflict_RetriesOnceAndSucceeds()
    {
        var productId = Guid.NewGuid();

        // Arrange: stream at V=1 (initialized).
        using (var setupScope = Fixture.CreateScope())
        {
            var setupRepo = setupScope.ServiceProvider.GetRequiredService<EventStoreRepository>();
            var init = await setupRepo.AppendAsync(
                productId,
                a => a.Initialize(productId, UtcNow),
                TestContext.Current.CancellationToken);
            init.Should().BeSuccess();
        }

        // Build a DbContext whose first SaveChangesAsync is intercepted to
        // stage a competing V=2 row (simulating a racing writer), then releases
        // control. The repo's own SaveChangesAsync collides on PK, detaches,
        // retries against the now-V=2 state, and commits at V=3.
        var competingEvent = new StockReceivedDomainEvent
        {
            ProductId = productId,
            Quantity = 5,
            Source = StockSource.ReceivingDock.Value,
            ReceivedByUserId = null,
            OccurredOnUtc = UtcNow,
        };

        var interceptor = new OneShotConflictInterceptor(
            ct => Fixture.InsertEventStoreRowAsync(productId, version: 2, @event: competingEvent, ct),
            fireCount: 1);

        await using var raceCtx = Fixture.CreateInterceptedDbContext(interceptor);
        var raceRepo = new EventStoreRepository(raceCtx, NoOpDomainEventDispatcher.Instance);

        var result = await raceRepo.AppendAsync(
            productId,
            a => a.ReceiveStock(100, StockSource.ReceivingDock, null, UtcNow),
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Version.Should().Be(3);
        result.Value.OnHand.Should().Be(105); // 5 (competing) + 100 (our retry)

        var rows = await ReadStreamAsync(productId);

        rows.Should().HaveCount(3);
        rows[0].Version.Should().Be(1);
        rows[1].Version.Should().Be(2);
        rows[2].Version.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "concurrency")]
    public async Task AppendAsync_ConcurrencyConflict_AfterRetry_ReturnsFailWithConcurrencyError()
    {
        var productId = Guid.NewGuid();

        using (var setupScope = Fixture.CreateScope())
        {
            var setupRepo = setupScope.ServiceProvider.GetRequiredService<EventStoreRepository>();
            var init = await setupRepo.AppendAsync(
                productId,
                a => a.Initialize(productId, UtcNow),
                TestContext.Current.CancellationToken);
            init.Should().BeSuccess();
        }

        // Inject a new competing row before BOTH attempts. Each competing
        // insert hops the version forward by one, so the repo exhausts its
        // one-retry budget and returns ConcurrencyError.
        var competingVersion = 1; // incremented on each injection to 2, then 3
        var interceptor = new OneShotConflictInterceptor(
            async ct =>
            {
                competingVersion++;
                var @event = new StockReceivedDomainEvent
                {
                    ProductId = productId,
                    Quantity = 1,
                    Source = StockSource.ReceivingDock.Value,
                    ReceivedByUserId = null,
                    OccurredOnUtc = UtcNow,
                };
                await Fixture.InsertEventStoreRowAsync(productId, competingVersion, @event, ct);
            },
            fireCount: 2);

        await using var raceCtx = Fixture.CreateInterceptedDbContext(interceptor);
        var raceRepo = new EventStoreRepository(raceCtx, NoOpDomainEventDispatcher.Instance);

        var result = await raceRepo.AppendAsync(
            productId,
            a => a.ReceiveStock(100, StockSource.ReceivingDock, null, UtcNow),
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ConcurrencyError>()
            .Which.ErrorCode.Should().Be("Inventory.Concurrency");
    }

    [Fact]
    public async Task AppendAsync_DelegateFailsFast_NoRowsAppended()
    {
        var productId = Guid.NewGuid();
        var reservationId = ReservationId.Create(Guid.NewGuid()).Value;
        var orderId = Guid.NewGuid();

        using (var setupScope = Fixture.CreateScope())
        {
            var setupRepo = setupScope.ServiceProvider.GetRequiredService<EventStoreRepository>();
            var setup = await setupRepo.AppendAsync(
                productId,
                a =>
                {
                    var init = a.Initialize(productId, UtcNow);
                    if (init.IsFailed)
                    {
                        return init;
                    }

                    return a.ReceiveStock(5, StockSource.ReceivingDock, null, UtcNow);
                },
                TestContext.Current.CancellationToken);
            setup.Should().BeSuccess();
        }

        using var scope = Fixture.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<EventStoreRepository>();

        // Reserve 10 against Available=5 — returns InsufficientStockError,
        // no event raised, repo must not touch the DB.
        var result = await repo.AppendAsync(
            productId,
            a => a.Reserve(reservationId, quantity: 10, orderId, TimeSpan.FromMinutes(15), UtcNow).ToResult(),
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<InsufficientStockError>();

        using var verifyScope = Fixture.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var rowCount = await verifyCtx.StockEvents
            .AsNoTracking()
            .CountAsync(r => r.StreamId == productId, TestContext.Current.CancellationToken);

        // Only the setup events (Init + Receive) — no reservation row.
        rowCount.Should().Be(2);
    }

    /// <summary>
    /// Example 2.3 of <c>docs/bc-design/example-mapping/inventory.md</c>. A deterministic version race has
    /// no outer entrance, so it is driven at the repository with an injected conflict.
    /// </summary>
    [Fact]
    [Trait("Category", "concurrency")]
    public async Task AppendAsync_ReserveLosesVersionRaceForLastUnits_RetriesThenFailsWithInsufficientStock()
    {
        // Arrange — stream at V=2 with OnHand=7, Reserved=0, Available=7.
        var productId = Guid.NewGuid();
        var winningReservationId = Guid.NewGuid();
        var winningOrderId = Guid.NewGuid();
        var losingReservationId = Guid.NewGuid();
        var losingOrderId = Guid.NewGuid();

        using (var setupScope = Fixture.CreateScope())
        {
            var setupRepo = setupScope.ServiceProvider.GetRequiredService<EventStoreRepository>();
            (await setupRepo.AppendAsync(
                productId,
                a => a.Initialize(productId, UtcNow.AddMinutes(-2)),
                TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await setupRepo.AppendAsync(
                productId,
                a => a.ReceiveStock(7, StockSource.ReceivingDock, null, UtcNow.AddMinutes(-1)),
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        // The competing write: the winner reserves 5 units at V=3.
        var winningReservedEvent = new StockReservedDomainEvent
        {
            ProductId = productId,
            ReservationId = winningReservationId,
            Quantity = 5,
            OrderId = winningOrderId,
            ExpiresAtUtc = UtcNow.AddMinutes(15),
            OccurredOnUtc = UtcNow,
        };
        var interceptor = new OneShotConflictInterceptor(
            ct => Fixture.InsertEventStoreRowAsync(productId, version: 3, @event: winningReservedEvent, ct),
            fireCount: 1);

        await using var raceCtx = Fixture.CreateInterceptedDbContext(interceptor);
        var raceRepo = new EventStoreRepository(raceCtx, NoOpDomainEventDispatcher.Instance);

        // Act — the loser also asks for 5; its retry rehydrates at V=3 (Available=2).
        var loserResult = await raceRepo.AppendAsync(
            productId,
            a => a.Reserve(
                ReservationId.Create(losingReservationId).Value,
                quantity: 5,
                losingOrderId,
                TimeSpan.FromMinutes(15),
                UtcNow).ToResult(),
            TestContext.Current.CancellationToken);

        // Assert — refused with the post-race Available, and the stream holds exactly the winner's reserve.
        loserResult.Should().BeFailure();
        loserResult.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<InsufficientStockError>()
            .Which.Available.Should().Be(2,
                "after the winner appended at V=3, the loser's retry rehydrates the up-to-date Available");

        var rows = await ReadStreamAsync(productId);
        rows.Should().HaveCount(3);
        rows[0].EventType.Should().BeEventType<StockItemInitializedDomainEvent>();
        rows[1].EventType.Should().BeEventType<StockReceivedDomainEvent>();
        rows[2].EventType.Should().BeEventType<StockReservedDomainEvent>();
        rows[2].Version.Should().Be(3);
    }

    /// <summary>
    /// Example 3.4 of <c>docs/bc-design/example-mapping/inventory.md</c>, driven at the repository for the
    /// same reason as Example 2.3 above.
    /// </summary>
    [Fact]
    [Trait("Category", "concurrency")]
    public async Task AppendAsync_ConfirmLosesVersionRaceToExpiryRelease_RetriesThenFailsWithReservationNotActive()
    {
        // Arrange — stream at V=3 with an Active reservation.
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using (var setupScope = Fixture.CreateScope())
        {
            var setupRepo = setupScope.ServiceProvider.GetRequiredService<EventStoreRepository>();
            (await setupRepo.AppendAsync(
                productId,
                a => a.Initialize(productId, UtcNow.AddMinutes(-3)),
                TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await setupRepo.AppendAsync(
                productId,
                a => a.ReceiveStock(5, StockSource.ReceivingDock, null, UtcNow.AddMinutes(-2)),
                TestContext.Current.CancellationToken)).Should().BeSuccess();
            (await setupRepo.AppendAsync(
                productId,
                a => a.Reserve(
                    ReservationId.Create(reservationId).Value,
                    quantity: 2,
                    orderId,
                    TimeSpan.FromMinutes(15),
                    UtcNow.AddMinutes(-1)).ToResult(),
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        // The competing write: the expiry worker's release wins at V=4.
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

        // Act
        var loserResult = await raceRepo.AppendAsync(
            productId,
            a => a.ConfirmReservation(ReservationId.Create(reservationId).Value, UtcNow),
            TestContext.Current.CancellationToken);

        // Assert — refused on the terminal status, and no ReservationConfirmedDomainEvent at any version.
        loserResult.Should().BeFailure();
        loserResult.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ReservationNotActiveError>()
            .Which.CurrentStatus.Should().Be(ReservationStatus.Released,
                "after the competing release wins at V=4, the retried Confirm rehydrates and sees the terminal Released status");

        var rows = await ReadStreamAsync(productId);
        rows.Should().HaveCount(4);
        rows[0].EventType.Should().BeEventType<StockItemInitializedDomainEvent>();
        rows[1].EventType.Should().BeEventType<StockReceivedDomainEvent>();
        rows[2].EventType.Should().BeEventType<StockReservedDomainEvent>();
        rows[3].Version.Should().Be(4);
        rows[3].EventType.Should().BeEventType<ReservationReleasedDomainEvent>(
            "exactly one resolution event — the competing release that won the version race");
    }

    private async Task<List<(int Version, string EventType)>> ReadStreamAsync(Guid productId)
    {
        var rows = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .Where(r => r.StreamId == productId)
            .OrderBy(r => r.Version)
            .Select(r => new { r.Version, r.EventType })
            .ToListAsync(TestContext.Current.CancellationToken);
        return rows.Select(r => (r.Version, r.EventType)).ToList();
    }
}
