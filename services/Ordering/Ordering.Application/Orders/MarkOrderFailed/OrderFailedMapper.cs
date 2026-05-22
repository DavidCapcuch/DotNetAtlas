using Ordering.Domain.Orders.Events;
using Ordering.Orders;
using Platform.SharedKernel.Exceptions;

namespace Ordering.Application.Orders.MarkOrderFailed;

/// <summary>
/// Maps <see cref="OrderFailedDomainEvent"/> → Avro <see cref="OrderFailedEvent"/>.
/// The domain event's <c>AtStatus</c> is the loosely-typed SmartEnum name
/// <see cref="string"/>; the Avro enum is <see cref="OrderStatusAtTransition"/>
/// constrained to 4 symbols (per events-catalog.md § 5.3.6). A mismatch is
/// bug-class — throws <see cref="DataIntegrityException"/> which routes the
/// message to the DLT via M4's Kafka middleware.
/// </summary>
internal static class OrderFailedMapper
{
    public static OrderFailedEvent ToOrderFailedEvent(this OrderFailedDomainEvent source) =>
        new()
        {
            OrderId = source.OrderId,
            CorrelationId = source.CorrelationId,
            BuyerId = source.BuyerId,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage,
            AtStatus = MapStatus(source.AtStatus),
            FailedAtUtc = source.FailedAtUtc.UtcDateTime,
        };

    // The FSM at OrderStatus.cs forbids Confirmed -> Failed (Confirmed's allowed
    // set is {Shipped, Cancelled}); Order.Fail's GuardTransition throws before
    // the mapper sees a Confirmed source. Only the three pre-confirmation
    // statuses can legitimately appear on an OrderFailedEvent.
    internal static OrderStatusAtTransition MapStatus(string name) => name switch
    {
        "Created" => OrderStatusAtTransition.Created,
        "StockReserved" => OrderStatusAtTransition.StockReserved,
        "PaymentCompleted" => OrderStatusAtTransition.PaymentCompleted,
        _ => throw new DataIntegrityException(
            "Order.InvalidAtStatusForFailure",
            $"OrderStatus '{name}' is not a valid AtStatus for OrderFailedEvent (allowed: Created, StockReserved, PaymentCompleted)."),
    };
}
