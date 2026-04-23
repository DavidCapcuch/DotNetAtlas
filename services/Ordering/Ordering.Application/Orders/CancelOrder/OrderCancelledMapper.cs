using Ordering.Domain.Orders.Events;
using Ordering.Orders;
using Platform.SharedKernel.Exceptions;

namespace Ordering.Application.Orders.CancelOrder;

/// <summary>
/// Maps <see cref="OrderCancelledDomainEvent"/> → Avro
/// <see cref="OrderCancelledEvent"/>. <c>AtStatus</c> is a SmartEnum name
/// string on the domain event; the Avro enum is a closed 4-symbol set
/// (events-catalog.md § 5.3.3). An unexpected value is bug-class.
/// </summary>
internal static class OrderCancelledMapper
{
    public static OrderCancelledEvent ToOrderCancelledEvent(this OrderCancelledDomainEvent source) =>
        new()
        {
            OrderId = source.OrderId,
            CorrelationId = source.CorrelationId,
            BuyerId = source.BuyerId,
            Reason = source.Reason,
            AtStatus = MapStatus(source.AtStatus),
            CancelledAtUtc = source.CancelledAtUtc.UtcDateTime,
        };

    internal static OrderStatusAtTransition MapStatus(string name) => name switch
    {
        "Created" => OrderStatusAtTransition.Created,
        "StockReserved" => OrderStatusAtTransition.StockReserved,
        "PaymentCompleted" => OrderStatusAtTransition.PaymentCompleted,
        "Confirmed" => OrderStatusAtTransition.Confirmed,
        _ => throw new DataIntegrityException(
            "Order.InvalidAtStatusForCancellation",
            $"OrderStatus '{name}' is not a valid AtStatus for OrderCancelledEvent (allowed: Created, StockReserved, PaymentCompleted, Confirmed)."),
    };
}
