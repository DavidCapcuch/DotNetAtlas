using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReleaseReservation;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Hand-rolled fluent seed for <c>StockItem</c> event streams. Each method
/// walks the aggregate through the production command handlers so the seed
/// produces real domain events / projection rows / outbox writes — i.e.
/// byte-identical to a production-emitted stream. Integration tests use
/// this to set up source-state preconditions without duplicating
/// init+receive+reserve boilerplate per test class.
/// </summary>
/// <remarks>
/// <para>
/// Each method creates a fresh DI scope per command so each handler runs on
/// its own <c>IInventoryDbContext</c> instance, matching production where
/// every command typically lands on its own HTTP-scoped DbContext. The
/// EventStoreRepository's <c>AsNoTracking</c> rehydrate and per-call
/// <c>SaveChangesAsync</c> mean cross-command isolation is correct even
/// though the underlying Postgres state is shared.
/// </para>
/// <para>
/// Mirrors <c>test/Ordering.IntegrationTests/Common/OrderSeed.cs</c>. Seeds
/// fail loudly via <c>.Should().BeSuccess()</c> so a broken precondition
/// surfaces in the seed, not later in the test body.
/// </para>
/// </remarks>
public sealed class StockItemSeed
{
    /// <summary>
    /// On-hand quantity used by the composite seeds when the caller doesn't
    /// override. Sized well above the per-call reserve quantities used in
    /// every test in the suite (typical reserve ≤ 5).
    /// </summary>
    public const int DefaultOnHand = 10;

    /// <summary>
    /// Reservation TTL used by the composite seeds when the caller doesn't
    /// override. Aligned with the saga's default reservation window.
    /// </summary>
    public static readonly TimeSpan DefaultReservationTtl = TimeSpan.FromMinutes(15);

    private const string DefaultReceivingSource = "receiving-dock";

    private readonly IntegrationTestFixture _fixture;

    public StockItemSeed(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Initializes a fresh stream (the V1 <c>StockItemInitializedDomainEvent</c>).</summary>
    public async Task InitializeAsync(Guid productId, DateTimeOffset occurredOnUtc, CancellationToken ct)
    {
        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();

        (await handler.HandleAsync(
            new InitializeStockItemCommand
            {
                ProductId = productId,
                OccurredOnUtc = occurredOnUtc,
            },
            ct)).Should().BeSuccess();
    }

    /// <summary>Receives stock — bumps OnHand and Available on the projection.</summary>
    public async Task ReceiveAsync(
        Guid productId,
        int quantity,
        DateTimeOffset occurredOnUtc,
        CancellationToken ct,
        string source = DefaultReceivingSource)
    {
        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();

        (await handler.HandleAsync(
            new ReceiveStockCommand
            {
                ProductId = productId,
                Quantity = quantity,
                Source = source,
                ReceivedByUserId = null,
                OccurredOnUtc = occurredOnUtc,
            },
            ct)).Should().BeSuccess();
    }

    /// <summary>Reserves stock — appends <c>StockReservedDomainEvent</c> and the audit row.</summary>
    public async Task ReserveAsync(
        Guid productId,
        Guid reservationId,
        Guid orderId,
        int quantity,
        TimeSpan timeToLive,
        DateTimeOffset occurredOnUtc,
        CancellationToken ct)
    {
        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReserveStockCommand>>();

        (await handler.HandleAsync(
            new ReserveStockCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                Quantity = quantity,
                OrderId = orderId,
                TimeToLive = timeToLive,
                OccurredOnUtc = occurredOnUtc,
            },
            ct)).Should().BeSuccess();
    }

    /// <summary>Releases an active reservation — appends <c>ReservationReleasedDomainEvent</c>.</summary>
    public async Task ReleaseAsync(
        Guid productId,
        Guid reservationId,
        ReleaseReason reason,
        DateTimeOffset occurredOnUtc,
        CancellationToken ct)
    {
        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReleaseReservationCommand>>();

        (await handler.HandleAsync(
            new ReleaseReservationCommand
            {
                ReservationId = reservationId,
                ProductId = productId,
                Reason = reason,
                OccurredOnUtc = occurredOnUtc,
            },
            ct)).Should().BeSuccess();
    }

    /// <summary>
    /// Walks the stream from empty through <c>Initialize</c> +
    /// <c>ReceiveStock</c> so the projection has <c>OnHand = onHand</c>,
    /// <c>Reserved = 0</c>, <c>Available = onHand</c>. The init lands at
    /// <paramref name="anchorUtc"/>; the receive lands one minute later.
    /// </summary>
    /// <remarks>
    /// When <paramref name="onHand"/> is <c>0</c>, only <c>Initialize</c> is
    /// emitted — matches the behaviour the inline seeds used to short-circuit
    /// the receive step.
    /// </remarks>
    public async Task ProductWithOnHandAsync(
        Guid productId,
        int onHand,
        DateTimeOffset anchorUtc,
        CancellationToken ct)
    {
        await InitializeAsync(productId, anchorUtc, ct);

        if (onHand > 0)
        {
            await ReceiveAsync(productId, onHand, anchorUtc.AddMinutes(1), ct);
        }
    }

    /// <summary>
    /// Walks the stream all the way to one <c>Active</c> reservation:
    /// <c>Initialize</c> @ <paramref name="anchorUtc"/>, <c>ReceiveStock</c>
    /// @ +1m, <c>ReserveStock</c> @ +2m. Override <paramref name="onHand"/>
    /// or <paramref name="timeToLive"/> when the test needs values other
    /// than the defaults.
    /// </summary>
    public async Task ActiveReservationAsync(
        Guid productId,
        Guid reservationId,
        Guid orderId,
        int quantity,
        DateTimeOffset anchorUtc,
        CancellationToken ct,
        int onHand = DefaultOnHand,
        TimeSpan? timeToLive = null)
    {
        await InitializeAsync(productId, anchorUtc, ct);
        await ReceiveAsync(productId, onHand, anchorUtc.AddMinutes(1), ct);
        await ReserveAsync(
            productId,
            reservationId,
            orderId,
            quantity,
            timeToLive ?? DefaultReservationTtl,
            anchorUtc.AddMinutes(2),
            ct);
    }

    /// <summary>
    /// Extends <see cref="ActiveReservationAsync"/> with a release event at
    /// <paramref name="anchorUtc"/> + 3m. Used by tests that exercise the
    /// "confirm-on-already-released" path.
    /// </summary>
    public async Task ReleasedReservationAsync(
        Guid productId,
        Guid reservationId,
        Guid orderId,
        int quantity,
        ReleaseReason reason,
        DateTimeOffset anchorUtc,
        CancellationToken ct,
        int onHand = DefaultOnHand,
        TimeSpan? timeToLive = null)
    {
        await ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity,
            anchorUtc,
            ct,
            onHand,
            timeToLive);

        await ReleaseAsync(productId, reservationId, reason, anchorUtc.AddMinutes(3), ct);
    }
}
