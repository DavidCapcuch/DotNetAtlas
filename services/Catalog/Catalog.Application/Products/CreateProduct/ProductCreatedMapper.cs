using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.SharedKernel.Exceptions;
using AvroProductCreatedEvent = Catalog.Products.ProductCreatedEvent;
using AvroProductStatus = Catalog.Products.ProductStatus;
using DomainProductStatus = Catalog.Domain.Products.ValueObjects.ProductStatus;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Maps the <see cref="Product"/> aggregate + its <see cref="Category"/> to the external Avro
/// <see cref="AvroProductCreatedEvent"/>, which carries fields enriched from the aggregate and its
/// category (e.g. CategoryPath, Status, CreatedAtUtc) that the internal domain event doesn't hold.
/// </summary>
internal static class ProductCreatedMapper
{
    /// <summary>
    /// Caps the external event's <c>Description</c> for consumer convenience — independent of, and
    /// shorter than, the domain's own <see cref="Catalog.Domain.Products.ValueObjects.ProductDescription"/>
    /// length, so a longer stored description is truncated rather than rejected.
    /// </summary>
    private const int MaxDescriptionLength = 1000;

    /// <summary>
    /// Scale pinned by <c>PriceAmount</c> in <c>ProductCreatedEvent.avsc</c> (<c>decimal(19,4)</c>).
    /// Avro rejects a datum whose scale differs from the schema's, so the amount must be normalised
    /// rather than inheriting the .NET decimal's own scale.
    /// </summary>
    private const int MoneyScale = 4;

    public static AvroProductCreatedEvent ToProductCreatedEvent(this Product product, Category category) =>
        new()
        {
            ProductId = product.Id,
            Sku = product.Sku.Value,
            Name = product.Name.Value,
            Description = TruncateDescription(product.Description.Value),
            CategoryId = product.CategoryId,
            CategoryPath = category.Path.Value,
            BrandName = product.Brand.Value,
            PriceAmount = product.Price.Amount.ToAvroDecimal(MoneyScale),
            PriceCurrency = product.Price.Currency.Name,
            Status = ToAvroStatus(product.Status),
            CreatedAtUtc = product.CreatedUtc.UtcDateTime,
        };

    private static string TruncateDescription(string description)
    {
        if (string.IsNullOrEmpty(description) || description.Length <= MaxDescriptionLength)
        {
            return description;
        }

        return description[..MaxDescriptionLength];
    }

    private static AvroProductStatus ToAvroStatus(DomainProductStatus status)
    {
        if (status == DomainProductStatus.Active)
        {
            return AvroProductStatus.Active;
        }

        if (status == DomainProductStatus.Discontinued)
        {
            return AvroProductStatus.Discontinued;
        }

        throw new DataIntegrityException(
            "Catalog.UnknownProductStatus",
            $"Unknown ProductStatus '{status.Name}' encountered during Avro mapping.");
    }
}
