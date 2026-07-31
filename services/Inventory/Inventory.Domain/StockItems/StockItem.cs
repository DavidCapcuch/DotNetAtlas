using FluentResults;
using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Inventory.Domain.StockItems;

/// <summary>
/// Event-sourced aggregate representing all physical and logical state for one
/// product's stock. Identity is <see cref="AggregateRoot{TId}.Id"/> (= <c>ProductId</c>,
/// shared with Catalog's <c>Product</c>). State is derived by folding the event stream;
/// no snapshot is persisted.
/// </summary>
/// <remarks>
/// Command methods produce exactly one <c>*DomainEvent</c>, add it to the domain-event list
/// via <see cref="AggregateRoot{TId}.AddDomainEvent(DomainEvent)"/>, and mutate
/// state through the same <c>Apply</c> reducer used by <see cref="Fold"/>. In-memory
/// state and the emitted event stream stay in sync by construction.
/// <para>
/// Business-expected failures (<c>Available &lt; quantity</c>, terminal-status reservation)
/// return <see cref="Result.Fail(FluentResults.IError)"/> with an <see cref="InsufficientStockError"/>
/// or <see cref="ReservationNotActiveError"/>. Invariant violations (unknown reservation,
/// re-initialize, negative stock) throw <see cref="DataIntegrityException"/> — saga
/// and command handlers guard against these upstream.
/// </para>
/// <para>
/// Reducer semantics per <c>docs/bc-design/inventory.md</c> § 5.
/// </para>
/// </remarks>
public sealed class StockItem : AggregateRoot<Guid>
{
    private readonly Dictionary<ReservationId, ReservationInfo> _reservations = new();

    private StockItem()
    {
    }

    /// <summary>Alias for <see cref="AggregateRoot{TId}.Id"/>; the event stream's <c>StreamId</c>.</summary>
    public Guid ProductId => Id;

    public int OnHand { get; private set; }

    public int Reserved { get; private set; }

    public int Available => OnHand - Reserved;

    public int Version { get; private set; }

    public IReadOnlyDictionary<ReservationId, ReservationInfo> Reservations => _reservations;

    /// <summary>
    /// Rehydrates a <see cref="StockItem"/> by folding the given event stream in order.
    /// Pure: same events → same aggregate state, always. An empty stream returns a
    /// fresh aggregate at <c>Version=0</c> (uninitialized).
    /// </summary>
    public static StockItem Fold(IEnumerable<DomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var item = new StockItem();
        foreach (var @event in events)
        {
            item.Apply(@event);
        }

        return item;
    }

    // ---------- Command methods ----------

    /// <summary>
    /// Initializes a brand-new stream for <paramref name="productId"/>.
    /// Precondition: <see cref="Version"/> == 0.
    /// </summary>
    public Result Initialize(Guid productId, DateTimeOffset occurredOnUtc)
    {
        if (productId == Guid.Empty)
        {
            throw new DataIntegrityException(
                "Inventory.ProductIdRequired",
                "ProductId must not be the empty Guid when initializing a stock item.");
        }

        if (Version != 0)
        {
            throw new DataIntegrityException(
                "Inventory.StreamAlreadyInitialized",
                $"Stream {Id} is already initialized at version {Version}.");
        }

        Raise(new StockItemInitializedDomainEvent
        {
            ProductId = productId,
            OccurredOnUtc = occurredOnUtc,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Records an inbound stock movement. Precondition: stream initialized; quantity &gt; 0.
    /// The <see cref="StockSource"/> value object is validated at construction time
    /// (see <see cref="StockSource.Create(string?)"/>) so the aggregate does not re-check.
    /// </summary>
    public Result ReceiveStock(int quantity, StockSource source, Guid? receivedByUserId, DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        EnsureInitialized();

        if (quantity <= 0)
        {
            throw new DataIntegrityException(
                "Inventory.QuantityMustBePositive",
                $"ReceiveStock quantity must be positive; got {quantity}.");
        }

        Raise(new StockReceivedDomainEvent
        {
            ProductId = Id,
            Quantity = quantity,
            Source = source.Value,
            ReceivedByUserId = receivedByUserId,
            OccurredOnUtc = occurredOnUtc,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Places a hold of <paramref name="quantity"/> units for the given order.
    /// Returns <see cref="Result.Fail(FluentResults.IError)"/> with an
    /// <see cref="InsufficientStockError"/> when <c>Available &lt; quantity</c> —
    /// this is business-expected, not a bug.
    /// </summary>
    public Result<ReservationInfo> Reserve(
        ReservationId reservationId,
        int quantity,
        Guid orderId,
        TimeSpan ttl,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(reservationId);

        EnsureInitialized();

        if (quantity <= 0)
        {
            throw new DataIntegrityException(
                "Inventory.QuantityMustBePositive",
                $"Reserve quantity must be positive; got {quantity}.");
        }

        if (orderId == Guid.Empty)
        {
            throw new DataIntegrityException(
                "Inventory.OrderIdRequired",
                "Reserve requires a non-empty OrderId.");
        }

        if (ttl <= TimeSpan.Zero)
        {
            throw new DataIntegrityException(
                "Inventory.TtlMustBePositive",
                $"Reserve TTL must be positive; got {ttl}.");
        }

        if (_reservations.ContainsKey(reservationId))
        {
            throw new DataIntegrityException(
                "Inventory.ReservationAlreadyExists",
                $"ReservationId {reservationId} already exists on stream {Id}.");
        }

        if (Available < quantity)
        {
            return Result.Fail<ReservationInfo>(
                InventoryErrors.InsufficientStock(Id, quantity, Available));
        }

        Raise(new StockReservedDomainEvent
        {
            ProductId = Id,
            ReservationId = reservationId.Value,
            Quantity = quantity,
            OrderId = orderId,
            ExpiresAtUtc = occurredOnUtc + ttl,
            OccurredOnUtc = occurredOnUtc,
        });

        return Result.Ok(_reservations[reservationId]);
    }

    /// <summary>
    /// Confirms a previously-placed reservation — stock physically leaves the warehouse.
    /// Idempotent: a duplicate confirm on an already-<see cref="ReservationStatus.Confirmed"/>
    /// reservation is a no-op (<see cref="Result.Ok"/>, no event). Confirming a
    /// <see cref="ReservationStatus.Released"/> reservation returns a
    /// <see cref="ReservationNotActiveError"/>. Unknown reservation ids throw
    /// <see cref="DataIntegrityException"/>.
    /// </summary>
    public Result ConfirmReservation(ReservationId reservationId, DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(reservationId);

        EnsureInitialized();

        if (!_reservations.TryGetValue(reservationId, out var reservation))
        {
            throw new DataIntegrityException(
                "Inventory.ReservationUnknown",
                $"ReservationId {reservationId} does not exist on stream {Id}.");
        }

        switch (reservation.Status)
        {
            case ReservationStatus.Confirmed:
                // Idempotent replay — same terminal state; no event.
                return Result.Ok();

            case ReservationStatus.Released:
                return Result.Fail(InventoryErrors.ReservationNotActive(Id, reservationId.Value, reservation.Status));

            case ReservationStatus.Active:
            default:
                break;
        }

        Raise(new ReservationConfirmedDomainEvent
        {
            ProductId = Id,
            ReservationId = reservationId.Value,
            ConfirmedAtUtc = occurredOnUtc,
            OccurredOnUtc = occurredOnUtc,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Releases a reservation without shipping — compensation, expiry, or cancellation.
    /// Idempotent on an already-<see cref="ReservationStatus.Released"/> reservation
    /// (<see cref="Result.Ok"/>, no event) per example-mapping Session 1.R5. Releasing
    /// an already-<see cref="ReservationStatus.Confirmed"/> reservation returns a
    /// <see cref="ReservationNotActiveError"/>. Unknown reservation ids throw
    /// <see cref="DataIntegrityException"/>.
    /// </summary>
    public Result ReleaseReservation(
        ReservationId reservationId,
        ReleaseReason reason,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(reservationId);

        EnsureInitialized();

        if (!_reservations.TryGetValue(reservationId, out var reservation))
        {
            throw new DataIntegrityException(
                "Inventory.ReservationUnknown",
                $"ReservationId {reservationId} does not exist on stream {Id}.");
        }

        switch (reservation.Status)
        {
            case ReservationStatus.Released:
                // Idempotent replay — reason is not re-checked because ReservationInfo
                // does not retain the original ReleaseReason (that lives on the stream +
                // ReservationAuditView). Safe because releases are at-least-once
                // (saga compensation + the TTL expiry worker).
                return Result.Ok();

            case ReservationStatus.Confirmed:
                return Result.Fail(InventoryErrors.ReservationNotActive(Id, reservationId.Value, reservation.Status));

            case ReservationStatus.Active:
            default:
                break;
        }

        Raise(new ReservationReleasedDomainEvent
        {
            ProductId = Id,
            ReservationId = reservationId.Value,
            ReleaseReason = reason,
            ReleasedAtUtc = occurredOnUtc,
            OccurredOnUtc = occurredOnUtc,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Admin correction — damage write-off, recount, transfer-out. Signed delta.
    /// A zero delta is a no-op (<see cref="Result.Ok"/>, no event). Any delta that
    /// would drive <c>OnHand</c> or <c>Available</c> below zero throws
    /// <see cref="DataIntegrityException"/> — admins must never make stock go negative.
    /// </summary>
    public Result AdjustStock(int delta, string reason, Guid? adjustedByUserId, DateTimeOffset occurredOnUtc)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DataIntegrityException(
                "Inventory.ReasonRequired",
                "AdjustStock reason must not be empty.");
        }

        if (delta == 0)
        {
            return Result.Ok();
        }

        var projectedOnHand = OnHand + delta;

        if (projectedOnHand < 0)
        {
            throw new DataIntegrityException(
                "Inventory.AdjustmentBelowZero",
                $"AdjustStock would drive OnHand below zero on stream {Id}: {OnHand} + {delta} = {projectedOnHand}.");
        }

        if (projectedOnHand - Reserved < 0)
        {
            throw new DataIntegrityException(
                "Inventory.AdjustmentBelowReservations",
                $"AdjustStock would drive Available below zero on stream {Id}: OnHand would be {projectedOnHand}, Reserved is {Reserved}.");
        }

        Raise(new StockAdjustedDomainEvent
        {
            ProductId = Id,
            Delta = delta,
            Reason = reason,
            AdjustedByUserId = adjustedByUserId,
            OccurredOnUtc = occurredOnUtc,
        });

        return Result.Ok();
    }

    // ---------- Reducer ----------

    /// <summary>
    /// Raise = apply the event to state AND add it to the outgoing domain-event list.
    /// </summary>
    private void Raise(DomainEvent @event)
    {
        Apply(@event);
        AddDomainEvent(@event);
    }

    private void Apply(DomainEvent @event)
    {
        switch (@event)
        {
            case StockItemInitializedDomainEvent e:
                ApplyInitialized(e);
                break;
            case StockReceivedDomainEvent e:
                ApplyReceived(e);
                break;
            case StockReservedDomainEvent e:
                ApplyReserved(e);
                break;
            case ReservationConfirmedDomainEvent e:
                ApplyConfirmed(e);
                break;
            case ReservationReleasedDomainEvent e:
                ApplyReleased(e);
                break;
            case StockAdjustedDomainEvent e:
                ApplyAdjusted(e);
                break;
            default:
                throw new DataIntegrityException(
                    "Inventory.UnknownEventType",
                    $"Unknown event type '{@event.GetType().FullName}' on stream {Id}.");
        }

        Version++;
    }

    private void ApplyInitialized(StockItemInitializedDomainEvent e)
    {
        if (Version != 0)
        {
            throw new DataIntegrityException(
                "Inventory.StreamAlreadyInitialized",
                $"StockItemInitializedDomainEvent applied to stream {Id} already at version {Version}.");
        }

        Id = e.ProductId;
        OnHand = 0;
        Reserved = 0;
        _reservations.Clear();
    }

    private void ApplyReceived(StockReceivedDomainEvent e)
    {
        EnsureAppliedToInitialized(nameof(StockReceivedDomainEvent));
        OnHand += e.Quantity;
    }

    private void ApplyReserved(StockReservedDomainEvent e)
    {
        EnsureAppliedToInitialized(nameof(StockReservedDomainEvent));

        var ridResult = ReservationId.Create(e.ReservationId);
        if (ridResult.IsFailed)
        {
            throw new DataIntegrityException(
                "Inventory.ReservationIdInvalid",
                $"StockReservedDomainEvent on stream {Id} carries invalid ReservationId {e.ReservationId}.");
        }

        var rid = ridResult.Value;

        if (_reservations.ContainsKey(rid))
        {
            throw new DataIntegrityException(
                "Inventory.ReservationAlreadyExists",
                $"StockReservedDomainEvent on stream {Id} duplicates ReservationId {rid}.");
        }

        Reserved += e.Quantity;
        _reservations[rid] = ReservationInfo.Create(
            reservationId: rid,
            productId: e.ProductId,
            quantity: e.Quantity,
            orderId: e.OrderId,
            reservedAtUtc: e.OccurredOnUtc,
            expiresAtUtc: e.ExpiresAtUtc,
            status: ReservationStatus.Active);
    }

    private void ApplyConfirmed(ReservationConfirmedDomainEvent e)
    {
        EnsureAppliedToInitialized(nameof(ReservationConfirmedDomainEvent));

        var rid = ReservationIdOrThrow(e.ReservationId, nameof(ReservationConfirmedDomainEvent));

        if (!_reservations.TryGetValue(rid, out var reservation))
        {
            throw new DataIntegrityException(
                "Inventory.ReservationUnknown",
                $"ReservationConfirmedDomainEvent on stream {Id} references unknown ReservationId {rid}.");
        }

        if (reservation.Status != ReservationStatus.Active)
        {
            throw new DataIntegrityException(
                "Inventory.ReservationNotActive",
                $"ReservationConfirmedDomainEvent on stream {Id} targets reservation {rid} in status {reservation.Status}.");
        }

        OnHand -= reservation.Quantity;
        Reserved -= reservation.Quantity;
        _reservations[rid] = reservation with { Status = ReservationStatus.Confirmed };
    }

    private void ApplyReleased(ReservationReleasedDomainEvent e)
    {
        EnsureAppliedToInitialized(nameof(ReservationReleasedDomainEvent));

        var rid = ReservationIdOrThrow(e.ReservationId, nameof(ReservationReleasedDomainEvent));

        if (!_reservations.TryGetValue(rid, out var reservation))
        {
            throw new DataIntegrityException(
                "Inventory.ReservationUnknown",
                $"ReservationReleasedDomainEvent on stream {Id} references unknown ReservationId {rid}.");
        }

        if (reservation.Status != ReservationStatus.Active)
        {
            throw new DataIntegrityException(
                "Inventory.ReservationNotActive",
                $"ReservationReleasedDomainEvent on stream {Id} targets reservation {rid} in status {reservation.Status}.");
        }

        Reserved -= reservation.Quantity;
        _reservations[rid] = reservation with { Status = ReservationStatus.Released };
    }

    private void ApplyAdjusted(StockAdjustedDomainEvent e)
    {
        EnsureAppliedToInitialized(nameof(StockAdjustedDomainEvent));

        var projectedOnHand = OnHand + e.Delta;

        if (projectedOnHand < 0)
        {
            throw new DataIntegrityException(
                "Inventory.AdjustmentBelowZero",
                $"StockAdjustedDomainEvent on stream {Id} would drive OnHand below zero: {OnHand} + {e.Delta}.");
        }

        if (projectedOnHand - Reserved < 0)
        {
            throw new DataIntegrityException(
                "Inventory.AdjustmentBelowReservations",
                $"StockAdjustedDomainEvent on stream {Id} would drive Available below zero.");
        }

        OnHand = projectedOnHand;
    }

    // ---------- Helpers ----------

    private void EnsureInitialized()
    {
        if (Version == 0)
        {
            throw new DataIntegrityException(
                "Inventory.StreamNotInitialized",
                $"Stream {Id} has not been initialized. Issue InitializeStockItemCommand first.");
        }
    }

    private void EnsureAppliedToInitialized(string eventTypeName)
    {
        if (Version == 0)
        {
            throw new DataIntegrityException(
                "Inventory.StreamNotInitialized",
                $"{eventTypeName} applied to stream {Id} before StockItemInitializedDomainEvent.");
        }
    }

    private ReservationId ReservationIdOrThrow(Guid value, string eventTypeName)
    {
        var result = ReservationId.Create(value);
        if (result.IsFailed)
        {
            throw new DataIntegrityException(
                "Inventory.ReservationIdInvalid",
                $"{eventTypeName} on stream {Id} carries invalid ReservationId {value}.");
        }

        return result.Value;
    }
}
