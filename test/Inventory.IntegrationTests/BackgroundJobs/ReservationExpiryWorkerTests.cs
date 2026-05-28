using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.ConfirmReservation;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReleaseReservation;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.BackgroundJobs;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Platform.CQRS;

namespace Inventory.IntegrationTests.BackgroundJobs;

/// <summary>
/// Acceptance for <see cref="ReservationExpiryWorker"/>. Drives a single
/// <c>ProcessExpiredReservationsAsync</c> tick (bypassing the
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> loop) under a
/// local <see cref="FakeTimeProvider"/> so the audit-row scan
/// <c>Status='Active' AND ExpiresAtUtc &lt; now()</c> fires deterministically.
/// Covers the happy path (single expired reservation auto-released with
/// <see cref="ReleaseReason.Expiry"/>), batch fan-out, the
/// <c>ExpiresAtUtc &lt; now()</c> filter, and the two no-op races against
/// already-Released and already-Confirmed audit rows (DoD line 400 — race
/// between TTL expiry and confirm).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class ReservationExpiryWorkerTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset SeedUtc =
        new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(15);

    public ReservationExpiryWorkerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task SingleExpiredReservation_IsReleasedWithExpiryReason()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(productId, reservationId, orderId, quantity: 4, SeedUtc.AddMinutes(-2), TestContext.Current.CancellationToken, timeToLive: ReservationTtl);

        // 16 min after the reservation was created — past the 15-min TTL.
        var fakeTime = new FakeTimeProvider(SeedUtc.AddMinutes(16));

        await RunOneTickAsync(fakeTime);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Released);
        audit.ReleaseReason.Should().Be(ReleaseReason.Expiry);
        audit.ResolvedAtUtc.Should().NotBeNull();

        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == "Inventory.Reservations.ReservationReleasedEvent")
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle()
            .Which.TopicName.Should().Be("inventory.reservations");
    }

    [Fact]
    public async Task MultipleExpiredReservations_AllReleasedInOneTick()
    {
        var reservations = new[]
        {
            (ProductId: Guid.NewGuid(), ReservationId: Guid.NewGuid(), OrderId: Guid.NewGuid()),
            (ProductId: Guid.NewGuid(), ReservationId: Guid.NewGuid(), OrderId: Guid.NewGuid()),
            (ProductId: Guid.NewGuid(), ReservationId: Guid.NewGuid(), OrderId: Guid.NewGuid()),
        };

        foreach (var r in reservations)
        {
            await Seed.ActiveReservationAsync(r.ProductId, r.ReservationId, r.OrderId, quantity: 2, SeedUtc.AddMinutes(-2), TestContext.Current.CancellationToken, timeToLive: ReservationTtl);
        }

        var fakeTime = new FakeTimeProvider(SeedUtc.AddMinutes(16));

        await RunOneTickAsync(fakeTime);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var reservationIds = reservations.Select(r => r.ReservationId).ToArray();
        var audits = await db.ReservationAudit
            .AsNoTracking()
            .Where(r => reservationIds.Contains(r.ReservationId))
            .ToListAsync(TestContext.Current.CancellationToken);
        audits.Should().HaveCount(3);
        audits.Should().AllSatisfy(a =>
        {
            a.Status.Should().Be(ReservationStatus.Released);
            a.ReleaseReason.Should().Be(ReleaseReason.Expiry);
            a.ResolvedAtUtc.Should().NotBeNull();
        });

        var orderIdStrings = reservations.Select(r => r.OrderId.ToString()).ToArray();
        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => orderIdStrings.Contains(m.KafkaKey)
                && m.Type == "Inventory.Reservations.ReservationReleasedEvent")
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExpiredAndUnexpired_OnlyExpiredReleased()
    {
        // Old reservation: created at SeedUtc - 20m → expires at SeedUtc - 5m.
        var oldProductId = Guid.NewGuid();
        var oldReservationId = Guid.NewGuid();
        var oldOrderId = Guid.NewGuid();
        await Seed.ActiveReservationAsync(
            oldProductId, oldReservationId, oldOrderId, quantity: 2,
            SeedUtc.AddMinutes(-22), TestContext.Current.CancellationToken, timeToLive: ReservationTtl);

        // Fresh reservation: created at SeedUtc → expires at SeedUtc + 15m.
        var freshProductId = Guid.NewGuid();
        var freshReservationId = Guid.NewGuid();
        var freshOrderId = Guid.NewGuid();
        await Seed.ActiveReservationAsync(
            freshProductId, freshReservationId, freshOrderId, quantity: 2,
            SeedUtc.AddMinutes(-2), TestContext.Current.CancellationToken, timeToLive: ReservationTtl);

        // Tick at SeedUtc + 1m: only the old reservation has elapsed past expiry.
        var fakeTime = new FakeTimeProvider(SeedUtc.AddMinutes(1));

        await RunOneTickAsync(fakeTime);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var oldAudit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == oldReservationId, TestContext.Current.CancellationToken);
        oldAudit.Status.Should().Be(ReservationStatus.Released);
        oldAudit.ReleaseReason.Should().Be(ReleaseReason.Expiry);

        var freshAudit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == freshReservationId, TestContext.Current.CancellationToken);
        freshAudit.Status.Should().Be(ReservationStatus.Active);
        freshAudit.ReleaseReason.Should().BeNull();
        freshAudit.ResolvedAtUtc.Should().BeNull();

        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == freshOrderId.ToString()
                && m.Type == "Inventory.Reservations.ReservationReleasedEvent")
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().BeEmpty();
    }

    [Fact]
    public async Task AlreadyReleasedReservation_NoDoubleRelease()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(productId, reservationId, orderId, quantity: 2, SeedUtc.AddMinutes(-2), TestContext.Current.CancellationToken, timeToLive: ReservationTtl);

        // Pre-release via the saga compensation path so the audit row goes
        // Released/Compensation BEFORE the worker scans.
        using (var releaseScope = Fixture.CreateScope())
        {
            var releaseHandler = releaseScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReleaseReservationCommand>>();
            await releaseHandler.HandleAsync(
                new ReleaseReservationCommand
                {
                    ReservationId = reservationId,
                    ProductId = productId,
                    Reason = ReleaseReason.Compensation,
                    OccurredOnUtc = SeedUtc.AddMinutes(1),
                },
                TestContext.Current.CancellationToken);
        }

        // Now the audit row is Status=Released — even though ExpiresAtUtc is in
        // the past, the worker's WHERE Status='Active' filter MUST exclude it.
        var fakeTime = new FakeTimeProvider(SeedUtc.AddMinutes(20));

        await RunOneTickAsync(fakeTime);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Released);
        audit.ReleaseReason.Should().Be(ReleaseReason.Compensation);

        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == "Inventory.Reservations.ReservationReleasedEvent")
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle("the original Compensation release; no Expiry release");
    }

    [Fact]
    public async Task ConfirmedReservation_NotReleasedAfterExpiry()
    {
        // Race vs Confirm (DoD line 400): a reservation that confirmed before the
        // worker scan must never receive a phantom Expiry release, even if the
        // wall clock has moved past its original ExpiresAtUtc.
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(productId, reservationId, orderId, quantity: 2, SeedUtc.AddMinutes(-2), TestContext.Current.CancellationToken, timeToLive: ReservationTtl);

        using (var confirmScope = Fixture.CreateScope())
        {
            var confirmHandler = confirmScope.ServiceProvider
                .GetRequiredService<ICommandHandler<ConfirmReservationCommand>>();
            await confirmHandler.HandleAsync(
                new ConfirmReservationCommand
                {
                    ReservationId = reservationId,
                    ProductId = productId,
                    OccurredOnUtc = SeedUtc.AddMinutes(2),
                },
                TestContext.Current.CancellationToken);
        }

        // Advance past the original 15-min TTL.
        var fakeTime = new FakeTimeProvider(SeedUtc.AddMinutes(20));

        await RunOneTickAsync(fakeTime);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Confirmed);
        audit.ReleaseReason.Should().BeNull();

        var releasedRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString()
                && m.Type == "Inventory.Reservations.ReservationReleasedEvent")
            .ToListAsync(TestContext.Current.CancellationToken);
        releasedRows.Should().BeEmpty();
    }

    private async Task RunOneTickAsync(FakeTimeProvider fakeTime)
    {
        using var scope = Fixture.CreateScope();
        var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        var worker = new ReservationExpiryWorker(
            scopeFactory: scopeFactory,
            timeProvider: fakeTime,
            logger: NullLogger<ReservationExpiryWorker>.Instance);

        await worker.ProcessExpiredReservationsAsync(TestContext.Current.CancellationToken);
    }
}
