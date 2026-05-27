using Inventory.Application.Common.Data;
using Inventory.Application.Common.Messaging;
using Inventory.Application.Common.ReadModels;
using Inventory.Application.StockItems.ConfirmReservation;
using Inventory.Application.StockItems.ReleaseReservation;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Inventory.Application.StockItems;

/// <summary>
/// Owns the <c>inventory.reservation_audit</c> projection AND the
/// <c>inventory.reservations</c> external-event emission for every
/// reservation-lifecycle transition. One multiplexed handler keeps audit
/// updates + outbox writes co-located: a single event fires both.
/// </summary>
/// <remarks>
/// <para>
/// Events handled (all in <c>Inventory.Domain.StockItems.Events</c>):
/// <c>StockReservedEvent</c> → INSERT audit row, emit external
/// <see cref="Inventory.Reservations.StockReservedEvent"/>;
/// <c>ReservationConfirmedEvent</c> → UPDATE status=Confirmed, emit external
/// <see cref="Inventory.Reservations.ReservationConfirmedEvent"/>;
/// <c>ReservationReleasedEvent</c> → UPDATE status=Released, emit external
/// <see cref="Inventory.Reservations.ReservationReleasedEvent"/>.
/// </para>
/// <para>
/// Kafka key is always <c>OrderId</c> — enables saga fan-in of reservation
/// responses on a given order (events-catalog.md § 5.4).
/// </para>
/// </remarks>
internal sealed class ReservationLifecycleDomainEventHandler :
    IDomainEventHandler<StockReservedEvent>,
    IDomainEventHandler<ReservationConfirmedEvent>,
    IDomainEventHandler<ReservationReleasedEvent>
{
    private readonly IInventoryDbContext _db;
    private readonly ITransactionalOutbox<IInventoryDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<ReservationLifecycleDomainEventHandler> _logger;

    public ReservationLifecycleDomainEventHandler(
        IInventoryDbContext db,
        ITransactionalOutbox<IInventoryDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<ReservationLifecycleDomainEventHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(StockReservedEvent domainEvent, CancellationToken ct)
    {
        var row = new ReservationAuditRow
        {
            ReservationId = domainEvent.ReservationId,
            ProductId = domainEvent.ProductId,
            OrderId = domainEvent.OrderId,
            Quantity = domainEvent.Quantity,
            Status = ReservationStatus.Active,
            ReservedAtUtc = domainEvent.OccurredOnUtc,
            ExpiresAtUtc = domainEvent.ExpiresAtUtc,
            ResolvedAtUtc = null,
            ReleaseReason = null,
        };

        _db.ReservationAudit.Add(row);

        var avro = domainEvent.ToStockReservedEvent();
        _outbox.AddOutboxMessage(
            _topics.InventoryReservations,
            domainEvent.OrderId.ToString(),
            avro);

        _logger.LogDebug(
            "Queued StockReservedEvent for Order {OrderId} Reservation {ReservationId} ({Quantity} units)",
            domainEvent.OrderId, domainEvent.ReservationId, domainEvent.Quantity);

        return Task.CompletedTask;
    }

    public async Task Handle(ReservationConfirmedEvent domainEvent, CancellationToken ct)
    {
        var audit = await LoadAuditAsync(domainEvent.ReservationId, ct).ConfigureAwait(false);

        audit.Status = ReservationStatus.Confirmed;
        audit.ResolvedAtUtc = domainEvent.OccurredOnUtc;

        var avro = domainEvent.ToReservationConfirmedEvent(audit);
        _outbox.AddOutboxMessage(
            _topics.InventoryReservations,
            audit.OrderId.ToString(),
            avro);

        _logger.LogDebug(
            "Queued ReservationConfirmedEvent for Order {OrderId} Reservation {ReservationId}",
            audit.OrderId, domainEvent.ReservationId);
    }

    public async Task Handle(ReservationReleasedEvent domainEvent, CancellationToken ct)
    {
        var audit = await LoadAuditAsync(domainEvent.ReservationId, ct).ConfigureAwait(false);

        audit.Status = ReservationStatus.Released;
        audit.ResolvedAtUtc = domainEvent.OccurredOnUtc;
        audit.ReleaseReason = domainEvent.ReleaseReason;

        var avro = domainEvent.ToReservationReleasedEvent(audit);
        _outbox.AddOutboxMessage(
            _topics.InventoryReservations,
            audit.OrderId.ToString(),
            avro);

        _logger.LogDebug(
            "Queued ReservationReleasedEvent for Order {OrderId} Reservation {ReservationId} (reason={Reason})",
            audit.OrderId, domainEvent.ReservationId, domainEvent.ReleaseReason);
    }

    private async Task<ReservationAuditRow> LoadAuditAsync(Guid reservationId, CancellationToken ct)
    {
        var audit = await _db.ReservationAudit.FindAsync([reservationId], ct).ConfigureAwait(false);

        return audit ?? throw new DataIntegrityException(
            "Inventory.ReservationAuditRowMissing",
            $"reservation_audit row for Reservation {reservationId} is missing; StockReservedEvent must precede confirm/release for the same reservation.");
    }
}
