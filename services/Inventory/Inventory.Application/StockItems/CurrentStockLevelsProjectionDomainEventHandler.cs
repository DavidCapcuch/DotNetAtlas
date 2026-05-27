using Inventory.Application.Common.Data;
using Inventory.Application.Common.Messaging;
using Inventory.Application.Common.ReadModels;
using Inventory.Domain.StockItems.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Inventory.Application.StockItems;

/// <summary>
/// Maintains <c>inventory.current_stock_levels</c> — the hot-path projection
/// described in <c>inventory.md</c> § 9.1 — and emits the threshold-crossing
/// external <c>StockLevelChanged</c> event when <c>Available</c> transitions
/// between zero and positive.
/// </summary>
/// <remarks>
/// <para>
/// One multiplexed handler covers all 6 ES events. Running inside the
/// <c>EventStoreRepository.AppendAsync</c> dispatch loop, its
/// <see cref="IInventoryDbContext"/> writes are tracked alongside the
/// <c>stock_events</c> inserts and commit atomically in the same
/// <c>SaveChangesAsync</c>.
/// </para>
/// <para>
/// The handler records <see cref="CurrentStockLevelRow.PreviousAvailable"/>
/// as the row's <c>Available</c> value BEFORE the event is applied, so the
/// threshold-emission decision is a pure function of the row fields
/// (<c>PreviousAvailable</c> vs <c>Available</c>) — no need to rehydrate state
/// from the event stream or enumerate prior events.
/// </para>
/// <para>
/// Per <c>inventory.md</c> § 6.1 the external <c>StockLevelChanged</c> fires
/// ONLY on <c>0 &lt;-&gt; positive</c> transitions, not on every stock movement.
/// </para>
/// </remarks>
internal sealed class CurrentStockLevelsProjectionDomainEventHandler :
    IDomainEventHandler<StockItemInitializedEvent>,
    IDomainEventHandler<StockReceivedEvent>,
    IDomainEventHandler<StockReservedEvent>,
    IDomainEventHandler<ReservationConfirmedEvent>,
    IDomainEventHandler<ReservationReleasedEvent>,
    IDomainEventHandler<StockAdjustedEvent>
{
    private readonly IInventoryDbContext _db;
    private readonly ITransactionalOutbox<IInventoryDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<CurrentStockLevelsProjectionDomainEventHandler> _logger;

    public CurrentStockLevelsProjectionDomainEventHandler(
        IInventoryDbContext db,
        ITransactionalOutbox<IInventoryDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<CurrentStockLevelsProjectionDomainEventHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(StockItemInitializedEvent domainEvent, CancellationToken ct)
    {
        // First event on a brand-new stream — insert a zeroed row. No threshold
        // fires here: the row goes to Available=0 from "no row" (never
        // observed as positive) so the transition is not 0 <-> positive.
        var row = new CurrentStockLevelRow
        {
            ProductId = domainEvent.ProductId,
            OnHand = 0,
            Reserved = 0,
            Available = 0,
            PreviousAvailable = 0,
            LastUpdatedUtc = domainEvent.OccurredOnUtc,
            LastVersion = 1,
        };

        _db.CurrentStockLevels.Add(row);
        return Task.CompletedTask;
    }

    public async Task Handle(StockReceivedEvent domainEvent, CancellationToken ct)
    {
        var row = await LoadAsync(domainEvent.ProductId, ct).ConfigureAwait(false);
        var prev = row.Available;

        row.OnHand += domainEvent.Quantity;
        Apply(row, prev, domainEvent.OccurredOnUtc);

        MaybeEmitStockLevelChanged(row, prev, domainEvent.OccurredOnUtc);
    }

    public async Task Handle(StockReservedEvent domainEvent, CancellationToken ct)
    {
        var row = await LoadAsync(domainEvent.ProductId, ct).ConfigureAwait(false);
        var prev = row.Available;

        row.Reserved += domainEvent.Quantity;
        Apply(row, prev, domainEvent.OccurredOnUtc);

        MaybeEmitStockLevelChanged(row, prev, domainEvent.OccurredOnUtc);
    }

    public async Task Handle(ReservationConfirmedEvent domainEvent, CancellationToken ct)
    {
        var row = await LoadAsync(domainEvent.ProductId, ct).ConfigureAwait(false);
        var prev = row.Available;
        var qty = await LookupReservationQuantityAsync(domainEvent.ReservationId, ct).ConfigureAwait(false);

        // Confirm: stock physically leaves. OnHand -= qty AND Reserved -= qty.
        // Net Available (= OnHand - Reserved) is unchanged by this event alone,
        // so MaybeEmitStockLevelChanged will typically be a no-op here; kept
        // for completeness in case future invariants change.
        row.OnHand -= qty;
        row.Reserved -= qty;
        Apply(row, prev, domainEvent.OccurredOnUtc);

        MaybeEmitStockLevelChanged(row, prev, domainEvent.OccurredOnUtc);
    }

    public async Task Handle(ReservationReleasedEvent domainEvent, CancellationToken ct)
    {
        var row = await LoadAsync(domainEvent.ProductId, ct).ConfigureAwait(false);
        var prev = row.Available;
        var qty = await LookupReservationQuantityAsync(domainEvent.ReservationId, ct).ConfigureAwait(false);

        row.Reserved -= qty;
        Apply(row, prev, domainEvent.OccurredOnUtc);

        MaybeEmitStockLevelChanged(row, prev, domainEvent.OccurredOnUtc);
    }

    public async Task Handle(StockAdjustedEvent domainEvent, CancellationToken ct)
    {
        var row = await LoadAsync(domainEvent.ProductId, ct).ConfigureAwait(false);
        var prev = row.Available;

        row.OnHand += domainEvent.Delta;
        Apply(row, prev, domainEvent.OccurredOnUtc);

        MaybeEmitStockLevelChanged(row, prev, domainEvent.OccurredOnUtc);
    }

    private async Task<CurrentStockLevelRow> LoadAsync(Guid productId, CancellationToken ct)
    {
        var row = await _db.CurrentStockLevels.FindAsync([productId], ct).ConfigureAwait(false);
        if (row is null)
        {
            _logger.LogError(
                "Projection row missing for Product {ProductId} — StockItemInitializedEvent must precede every other ES event for this stream",
                productId);
            throw new DataIntegrityException(
                "Inventory.ProjectionRowMissing",
                $"current_stock_levels row for Product {productId} is missing; StockItemInitializedEvent must precede every other ES event.");
        }

        return row;
    }

    private async Task<int> LookupReservationQuantityAsync(Guid reservationId, CancellationToken ct)
    {
        // The audit row was committed during the initial StockReservedEvent's
        // transaction — by the time confirm / release lands, it is visible via
        // the current DbContext's FindAsync (hitting the DB, as this handler
        // runs on a fresh scoped DbContext that didn't see the earlier insert
        // in its change tracker).
        var audit = await _db.ReservationAudit.FindAsync([reservationId], ct).ConfigureAwait(false);
        if (audit is null)
        {
            _logger.LogError(
                "Reservation audit row missing for Reservation {ReservationId} — StockReservedEvent must precede confirm/release for the same reservation",
                reservationId);
            throw new DataIntegrityException(
                "Inventory.ReservationAuditRowMissing",
                $"reservation_audit row for Reservation {reservationId} is missing; StockReservedEvent must precede confirm/release for the same reservation.");
        }

        return audit.Quantity;
    }

    private static void Apply(CurrentStockLevelRow row, int previousAvailable, DateTimeOffset occurredOnUtc)
    {
        row.PreviousAvailable = previousAvailable;
        row.Available = row.OnHand - row.Reserved;
        row.LastUpdatedUtc = occurredOnUtc;
        row.LastVersion += 1;

        // Defensive: the aggregate enforces Available >= 0, but a future
        // projection-rebuild from a corrupted stream (or a yet-unwritten
        // projection-replay job) could produce a negative Available and
        // MaybeEmitStockLevelChanged would then misclassify a -1 -> 0
        // transition as "back in stock". Fail loud rather than silently
        // emit a wrong threshold event.
        if (row.Available < 0)
        {
            throw new DataIntegrityException(
                "Inventory.ProjectionAvailableNegative",
                $"current_stock_levels row for Product {row.ProductId} computed Available = {row.Available} (OnHand={row.OnHand}, Reserved={row.Reserved}); aggregate invariant violated.");
        }
    }

    private void MaybeEmitStockLevelChanged(CurrentStockLevelRow row, int previousAvailable, DateTimeOffset occurredOnUtc)
    {
        var wasZero = previousAvailable == 0;
        var isZero = row.Available == 0;
        if (wasZero == isZero)
        {
            // Either both zero or both positive — no threshold crossing.
            return;
        }

        var avro = row.ToStockLevelChanged(occurredOnUtc);
        _outbox.AddOutboxMessage(
            _topics.InventoryStockEvents,
            row.ProductId.ToString(),
            avro);

        _logger.LogDebug(
            "Queued StockLevelChanged for Product {ProductId} (Available {Previous} -> {Current})",
            row.ProductId, previousAvailable, row.Available);
    }
}
