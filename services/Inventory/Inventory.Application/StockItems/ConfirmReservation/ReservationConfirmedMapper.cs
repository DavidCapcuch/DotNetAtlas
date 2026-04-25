using Inventory.Application.Common.ReadModels;
using InternalReservationConfirmedEvent = Inventory.Domain.StockItems.Events.ReservationConfirmedEvent;

namespace Inventory.Application.StockItems.ConfirmReservation;

/// <summary>
/// Maps internal <see cref="InternalReservationConfirmedEvent"/> + the
/// <see cref="ReservationAuditRow"/> → external Avro
/// <see cref="Inventory.Reservations.ReservationConfirmedEvent"/>. The
/// internal event does not carry <c>OrderId</c>; it is read from the audit
/// projection (committed during the initial <c>StockReservedEvent</c>).
/// </summary>
internal static class ReservationConfirmedMapper
{
    public static Inventory.Reservations.ReservationConfirmedEvent ToReservationConfirmedEvent(
        this InternalReservationConfirmedEvent source,
        ReservationAuditRow audit) =>
        new()
        {
            ProductId = source.ProductId,
            ReservationId = source.ReservationId,
            OrderId = audit.OrderId,
            ConfirmedAtUtc = source.OccurredOnUtc.UtcDateTime,
        };
}
