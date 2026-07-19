using Avro;
using Basket.Domain.Baskets.Events;
using Basket.Domain.Baskets.ValueObjects;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Application.Baskets.Checkout;

/// <summary>
/// Hand-written projection from the in-process <see cref="BasketCheckedOutDomainEvent"/>
/// to the external Avro <see cref="Basket.Sessions.BasketCheckoutInitiatedEvent"/>.
/// </summary>
/// <remarks>
/// The Avro shape is specified in <c>events-catalog.md § 5.2.1</c>. The tricky bits
/// — <see cref="Money"/> → <see cref="AvroDecimal"/>, <see cref="DateTimeOffset"/> →
/// timestamp-millis-encoded <see cref="DateTime"/>, nested <see cref="Address"/> →
/// <see cref="Basket.Sessions.CheckoutAddress"/> — are explicit here so the mapping
/// is auditable. Mapperly was considered but a straight-line mapper is both simpler
/// and easier to assert against in the mapper unit tests.
/// </remarks>
internal static class BasketCheckoutInitiatedMapper
{
    /// <summary>
    /// Scale pinned by every money field in <c>BasketCheckoutInitiatedEvent.avsc</c>
    /// (<c>decimal(19,4)</c>). Avro rejects a datum whose scale differs from the schema's, so
    /// amounts must be normalised here rather than inheriting the .NET decimal's own scale.
    /// </summary>
    private const int MoneyScale = 4;

    /// <summary>
    /// Projects a <see cref="BasketCheckedOutDomainEvent"/> onto the Avro integration event.
    /// The basket-wide currency is read from <see cref="BasketTotal.Amount"/> — that's the
    /// authoritative single-currency value computed by the aggregate (uniformity invariant 5
    /// is already enforced upstream).
    /// </summary>
    public static Basket.Sessions.BasketCheckoutInitiatedEvent ToBasketCheckoutInitiatedEvent(
        this BasketCheckedOutDomainEvent src)
    {
        ArgumentNullException.ThrowIfNull(src);

        var snapshot = src.Snapshot;

        return new Basket.Sessions.BasketCheckoutInitiatedEvent
        {
            OrderId = src.OrderId,
            UserId = src.UserId,
            Items = snapshot.Items.Select(MapItem).ToList(),
            TotalAmount = snapshot.Total.Amount.Amount.ToAvroDecimal(MoneyScale),
            Currency = snapshot.Total.Amount.Currency.Name,
            ShippingAddress = MapAddress(src.ShippingAddress),
            BillingAddress = MapAddress(src.BillingAddress),
            PaymentMethodId = src.PaymentMethodId,
            InitiatedAtUtc = src.OccurredOnUtc.UtcDateTime,
        };
    }

    private static Basket.Sessions.BasketCheckoutItem MapItem(BasketItem item)
    {
        var unitAmount = item.Snapshot.Price.Amount;
        var lineTotal = unitAmount * item.Quantity;

        return new Basket.Sessions.BasketCheckoutItem
        {
            ProductId = item.ProductId,
            Sku = item.Snapshot.Sku,
            Name = item.Snapshot.Name,
            UnitPriceAmount = unitAmount.ToAvroDecimal(MoneyScale),
            UnitPriceCurrency = item.Snapshot.Price.Currency.Name,
            Quantity = item.Quantity,
            LineTotal = lineTotal.ToAvroDecimal(MoneyScale),
        };
    }

    private static Basket.Sessions.CheckoutAddress MapAddress(Address address)
    {
        return new Basket.Sessions.CheckoutAddress
        {
            Street1 = address.Street1,
            Street2 = address.Street2,
            City = address.City,
            State = address.State,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode,
        };
    }
}
