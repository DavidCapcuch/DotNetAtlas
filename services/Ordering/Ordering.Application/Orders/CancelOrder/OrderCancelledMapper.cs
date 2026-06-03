using Ordering.Domain.Orders.Events;
using Ordering.Domain.Orders.ValueObjects;
using Ordering.Orders;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Application.Orders.CancelOrder;

/// <summary>
/// Maps <see cref="OrderCancelledDomainEvent"/> → Avro
/// <see cref="OrderCancelledEvent"/>. Per ADR-0020 / Wave 1.6 the Avro event
/// is a Summary Event — Items, Total, and BillingAddress travel with it
/// alongside the original Reason / AtStatus delta payload. Scale-4 decimal
/// conversion via <see cref="AvroDecimalExtensions.ToAvroDecimal"/> is
/// mandatory; implicit <c>decimal → AvroDecimal</c> breaks serialization on
/// scale mismatch. <c>AtStatus</c> is a SmartEnum name string on the domain
/// event; the Avro enum is a closed 4-symbol set (events-catalog.md § 5.3.3).
/// An unexpected value is bug-class.
/// </summary>
public static class OrderCancelledMapper
{
    private const int Scale = 4;

    public static OrderCancelledEvent ToOrderCancelledEvent(this OrderCancelledDomainEvent source) =>
        new()
        {
            OrderId = source.OrderId,
            BuyerId = source.BuyerId,
            Reason = source.Reason,
            AtStatus = MapStatus(source.AtStatus),
            CancelledAtUtc = source.CancelledAtUtc.UtcDateTime,
            Items = source.Items.Select(MapItem).ToList(),
            TotalAmount = source.Total.Amount.ToAvroDecimal(Scale),
            Currency = source.Total.Currency.Name,
            BillingAddress = MapBillingAddress(source.BillingAddress),
        };

    private static OrderItemCancelled MapItem(OrderItem source) =>
        new()
        {
            ProductId = source.ProductId,
            Sku = source.ProductSnapshot.Sku,
            Name = source.ProductSnapshot.Name,
            Quantity = source.Quantity,
            UnitPriceAmount = source.UnitPrice.Amount.ToAvroDecimal(Scale),
            LineTotalAmount = source.LineTotal.Amount.ToAvroDecimal(Scale),
        };

    private static OrderCancellationBillingAddress MapBillingAddress(Address source) =>
        new()
        {
            Street1 = source.Street1,
            Street2 = source.Street2,
            City = source.City,
            State = source.State,
            PostalCode = source.PostalCode,
            CountryCode = source.CountryCode,
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
