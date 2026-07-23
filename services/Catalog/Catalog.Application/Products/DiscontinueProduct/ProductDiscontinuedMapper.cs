using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using AvroProductDiscontinuedEvent = Catalog.Products.ProductDiscontinuedEvent;

namespace Catalog.Application.Products.DiscontinueProduct;

/// <summary>
/// Maps the <see cref="Product"/> aggregate + <see cref="ProductDiscontinuedDomainEvent"/> to the
/// external Avro <see cref="AvroProductDiscontinuedEvent"/> — the aggregate is needed only for the
/// denormalised <c>Sku</c> the domain event doesn't carry.
/// </summary>
internal static class ProductDiscontinuedMapper
{
    public static AvroProductDiscontinuedEvent ToProductDiscontinuedEvent(
        this Product product,
        ProductDiscontinuedDomainEvent domainEvent) =>
        new()
        {
            ProductId = product.Id,
            Sku = product.Sku.Value,
            Reason = domainEvent.Reason,
            DiscontinuedAtUtc = domainEvent.OccurredOnUtc.UtcDateTime,
        };
}
