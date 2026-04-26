using Inventory.Application.Common.ReadModels;

namespace Inventory.Application.StockItems.Common;

internal static class ReservationAuditResponseMapper
{
    public static ReservationAuditResponse ToReservationAuditResponse(this ReservationAuditRow row) =>
        new()
        {
            ReservationId = row.ReservationId,
            ProductId = row.ProductId,
            OrderId = row.OrderId,
            Quantity = row.Quantity,
            Status = row.Status,
            ReservedAtUtc = row.ReservedAtUtc,
            ExpiresAtUtc = row.ExpiresAtUtc,
            ResolvedAtUtc = row.ResolvedAtUtc,
            ReleaseReason = row.ReleaseReason,
        };
}
