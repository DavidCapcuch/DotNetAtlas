namespace Inventory.Domain.StockItems.ValueObjects;

/// <summary>
/// Lifecycle of a single reservation. A reservation starts <see cref="Active"/>,
/// then resolves to either <see cref="Confirmed"/> (order shipped, stock decremented)
/// or <see cref="Released"/> (compensation, expiry, or cancellation).
/// </summary>
public enum ReservationStatus
{
    Active = 0,
    Confirmed = 1,
    Released = 2,
}
