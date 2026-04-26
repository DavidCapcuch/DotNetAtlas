using Inventory.Application.StockItems.Common;
using Platform.CQRS;

namespace Inventory.Application.StockItems.GetReservationById;

/// <summary>
/// Public query — read a single <c>reservation_audit</c> row by
/// <c>ReservationId</c>. Drives <c>GET /api/v1/inventory/reservations/{reservationId}</c>.
/// </summary>
public sealed class GetReservationByIdQuery : IQuery<ReservationAuditResponse>
{
    public required Guid ReservationId { get; init; }
}
