using Ordering.Domain.Orders.Events;
using Ordering.Orders;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Application.Orders.CreateOrder;

/// <summary>
/// Maps <see cref="OrderCreatedDomainEvent"/> to the external Avro
/// <see cref="OrderCreatedEvent"/>. Scale-4 decimal conversion via
/// <see cref="AvroDecimalExtensions.ToAvroDecimal"/> is mandatory —
/// implicit <c>decimal → AvroDecimal</c> breaks serialization on scale
/// mismatch. Shipping / billing addresses are deliberately NOT mapped —
/// the Avro event does not carry them (they are PII kept out of the
/// checkout saga flow per ADR-0011 v1 policy; event-catalog § 5.3.1).
/// </summary>
public static class OrderCreatedMapper
{
    private const int Scale = 4;

    public static OrderCreatedEvent ToOrderCreatedEvent(this OrderCreatedDomainEvent source) =>
        new()
        {
            OrderId = source.OrderId,
            BuyerId = source.BuyerId,
            PaymentMethodId = source.PaymentMethodId,
            TotalAmount = source.Total.Amount.ToAvroDecimal(Scale),
            Currency = source.Total.Currency.Name,
            CreatedAtUtc = source.CreatedAtUtc.UtcDateTime,
            Items = source.Items.Select(MapItem).ToList(),
        };

    private static OrderItemCreated MapItem(OrderCreatedDomainEventItem source) =>
        new()
        {
            ProductId = source.ProductId,
            Sku = source.Sku,
            Name = source.Name,
            Quantity = source.Quantity,
            UnitPriceAmount = source.UnitPriceAmount.ToAvroDecimal(Scale),
            LineTotalAmount = source.LineTotalAmount.ToAvroDecimal(Scale),
        };
}
