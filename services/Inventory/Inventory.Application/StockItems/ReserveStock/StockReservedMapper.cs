using InternalStockReservedEvent = Inventory.Domain.StockItems.Events.StockReservedEvent;

namespace Inventory.Application.StockItems.ReserveStock;

/// <summary>
/// Maps internal <see cref="InternalStockReservedEvent"/> → external Avro
/// <see cref="Inventory.Reservations.StockReservedEvent"/>. 1:1 projection —
/// all fields already carry through on the internal event.
/// </summary>
internal static class StockReservedMapper
{
    public static Inventory.Reservations.StockReservedEvent ToStockReservedEvent(
        this InternalStockReservedEvent source) =>
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
