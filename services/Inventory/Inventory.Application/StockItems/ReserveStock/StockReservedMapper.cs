using Inventory.Domain.StockItems.Events;

namespace Inventory.Application.StockItems.ReserveStock;

/// <summary>
/// Maps internal <see cref="StockReservedDomainEvent"/> → external Avro
/// <see cref="Inventory.Reservations.StockReservedEvent"/>. 1:1 projection —
/// all fields already carry through on the internal event.
/// </summary>
internal static class StockReservedMapper
{
    public static Inventory.Reservations.StockReservedEvent ToStockReservedEvent(
        this StockReservedDomainEvent source) =>
        new()
        {
            ProductId = source.ProductId,
            ReservationId = source.ReservationId,
            OrderId = source.OrderId,
            Quantity = source.Quantity,
            ExpiresAtUtc = source.ExpiresAtUtc.UtcDateTime,
            ReservedAtUtc = source.OccurredOnUtc.UtcDateTime,
        };
}
