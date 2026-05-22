using Ordering.Domain.Orders.Events;
using Ordering.Domain.Orders.ValueObjects;
using Ordering.Orders;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.SharedKernel.ValueObjects;
using Riok.Mapperly.Abstractions;

namespace Ordering.Application.Orders.ConfirmOrder;

/// <summary>
/// Maps <see cref="OrderConfirmedDomainEvent"/> → Avro
/// <see cref="OrderConfirmedEvent"/>. Per ADR-0020 the Avro event is a Summary
/// Event — Items, Total, and BillingAddress travel with it. Scale-4 decimal
/// conversion via <see cref="AvroDecimalExtensions.ToAvroDecimal"/> is
/// mandatory; implicit <c>decimal → AvroDecimal</c> breaks serialization on
/// scale mismatch.
/// </summary>
[Mapper]
public static partial class OrderConfirmedMapper
{
    private const int Scale = 4;

    public static OrderConfirmedEvent ToOrderConfirmedEvent(this OrderConfirmedDomainEvent source) =>
        new()
        {
            OrderId = source.OrderId,
            CorrelationId = source.CorrelationId,
            BuyerId = source.BuyerId,
            ConfirmedAtUtc = source.ConfirmedAtUtc.UtcDateTime,
            Items = source.Items.Select(MapItem).ToList(),
            TotalAmount = source.Total.Amount.ToAvroDecimal(Scale),
            Currency = source.Total.Currency.Name,
            BillingAddress = MapBillingAddress(source.BillingAddress),
        };

    [UserMapping]
    private static OrderItemConfirmed MapItem(OrderItem source) =>
        new()
        {
            ProductId = source.ProductId,
            Sku = source.ProductSnapshot.Sku,
            Name = source.ProductSnapshot.Name,
            Quantity = source.Quantity,
            UnitPriceAmount = source.UnitPrice.Amount.ToAvroDecimal(Scale),
            LineTotalAmount = source.LineTotal.Amount.ToAvroDecimal(Scale),
        };

    [UserMapping]
    private static OrderBillingAddress MapBillingAddress(Address source) =>
        new()
        {
            Street1 = source.Street1,
            Street2 = source.Street2,
            City = source.City,
            State = source.State,
            PostalCode = source.PostalCode,
            CountryCode = source.CountryCode,
        };
}
