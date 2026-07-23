using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using AvroProductPriceChanged = Catalog.Products.ProductPriceChangedEvent;

namespace Catalog.Application.Products.UpdateProductPrice;

/// <summary>
/// Maps the <see cref="Product"/> aggregate + <see cref="ProductPriceChangedDomainEvent"/> to the
/// external Avro <see cref="AvroProductPriceChanged"/>. The aggregate supplies the denormalised
/// <c>Sku</c>; the domain event carries both prices and the timestamp. <c>Currency</c> reflects the
/// new price.
/// </summary>
internal static class ProductPriceChangedMapper
{
    /// <summary>
    /// Scale pinned by the money fields in <c>ProductPriceChangedEvent.avsc</c>
    /// (<c>decimal(19,4)</c>). Avro rejects a datum whose scale differs from the schema's, so
    /// amounts must be normalised rather than inheriting the .NET decimal's own scale.
    /// </summary>
    private const int MoneyScale = 4;

    public static AvroProductPriceChanged ToProductPriceChangedEvent(
        this Product product,
        ProductPriceChangedDomainEvent domainEvent) =>
        new()
        {
            ProductId = product.Id,
            Sku = product.Sku.Value,
            OldPriceAmount = domainEvent.OldPrice.Amount.ToAvroDecimal(MoneyScale),
            NewPriceAmount = domainEvent.NewPrice.Amount.ToAvroDecimal(MoneyScale),
            Currency = domainEvent.NewPrice.Currency.Name,
            ChangedAtUtc = domainEvent.OccurredOnUtc.UtcDateTime,
        };
}
