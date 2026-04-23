namespace Basket.Application.Baskets.Common.Contracts;

/// <summary>
/// Request-side postal address DTO — collected by the BFF/client at checkout time
/// and threaded through the <c>CheckoutBasketCommand</c>. Mirrors the Avro
/// <c>CheckoutAddress</c> record (<c>platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc</c>)
/// field-for-field so the mapper from command → aggregate → Avro is trivial.
/// </summary>
/// <remarks>
/// Per [ADR-0005](../../../../../docs/adr/0005-customer-data-in-ordering.md), Basket
/// validates only basic shape — <see cref="Street1"/>/<see cref="City"/>/<see cref="PostalCode"/>
/// non-empty and <see cref="CountryCode"/> 2-char ISO 3166-1 alpha-2. Deeper validation
/// (deliverability, country-specific postal code rules) lives in Ordering / external
/// services, not here.
/// </remarks>
public sealed record CheckoutAddressDto
{
    public required string Street1 { get; init; }

    public string? Street2 { get; init; }

    public required string City { get; init; }

    public string? State { get; init; }

    public required string PostalCode { get; init; }

    public required string CountryCode { get; init; }
}
