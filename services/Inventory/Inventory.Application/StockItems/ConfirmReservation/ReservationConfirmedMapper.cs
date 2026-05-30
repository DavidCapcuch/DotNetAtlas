using Inventory.Application.Common.ReadModels;
using Inventory.Domain.StockItems.Events;

namespace Inventory.Application.StockItems.ConfirmReservation;

/// <summary>
/// Maps internal <see cref="ReservationConfirmedDomainEvent"/> + the
/// <see cref="ReservationAuditRow"/> → external Avro
/// <see cref="Inventory.Reservations.ReservationConfirmedEvent"/>. The
/// internal event does not carry <c>OrderId</c>; it is read from the audit
/// projection (committed during the initial <c>StockReservedDomainEvent</c>).
/// </summary>
internal static class ReservationConfirmedMapper
{
    public static Inventory.Reservations.ReservationConfirmedEvent ToReservationConfirmedEvent(
        this ReservationConfirmedDomainEvent source,
        ReservationAuditRow audit) =>
        new()
        {
            ProductId = source.ProductId,
            ReservationId = source.ReservationId,
            OrderId = audit.OrderId,
            ConfirmedAtUtc = source.OccurredOnUtc.UtcDateTime,
        };
}
