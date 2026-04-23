using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderStockReserved;

/// <summary>
/// Saga-issued command after Inventory confirms the reservation. Transitions
/// the <c>Order</c> to <c>OrderStatus.StockReserved</c>. Audit-only — no
/// external event is emitted (the saga already observed Inventory's own
/// <c>StockReservedEvent</c>; see ordering.md § 6 consumer table).
/// </summary>
public sealed class MarkOrderStockReservedCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public required Guid ReservationId { get; init; }
}
