using System.Linq.Expressions;
using Catalog.Application.Common.Contracts;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// SQL-side projection of <see cref="ProductSearchViewRow"/> carrying exactly the columns the
/// product-detail response needs — everything except <c>IsSellable</c>.
/// Shared by <c>GetProductByIdQueryHandler</c> and <c>GetProductsByIdsQueryHandler</c> (ADR-0021)
/// so neither materializes the full read-model row. The raw <c>ImagesJson</c> string is carried
/// through and deserialized in <see cref="ToResponse"/> after the SQL projection.
/// </summary>
internal sealed record ProductDetailRow(
    Guid ProductId,
    string Sku,
    string Name,
    string Description,
    Guid CategoryId,
    string CategoryPath,
    string CategoryBreadcrumb,
    string BrandName,
    decimal PriceAmount,
    string PriceCurrency,
    string Status,
    decimal? DimensionsLength,
    decimal? DimensionsWidth,
    decimal? DimensionsHeight,
    string? DimensionsUnit,
    string ImagesJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUpdatedAtUtc)
{
    public static Expression<Func<ProductSearchViewRow, ProductDetailRow>> Projection => row =>
        new ProductDetailRow(
            row.ProductId,
            row.Sku,
            row.Name,
            row.Description,
            row.CategoryId,
            row.CategoryPath,
            row.CategoryBreadcrumb,
            row.BrandName,
            row.PriceAmount,
            row.PriceCurrency,
            row.Status,
            row.DimensionsLength,
            row.DimensionsWidth,
            row.DimensionsHeight,
            row.DimensionsUnit,
            row.ImagesJson,
            row.CreatedAtUtc,
            row.LastUpdatedAtUtc);

    public ProductDetailResponse ToResponse() =>
        new()
        {
            ProductId = ProductId,
            Sku = Sku,
            Name = Name,
            Description = Description,
            CategoryId = CategoryId,
            CategoryPath = CategoryPath,
            CategoryBreadcrumb = CategoryBreadcrumb,
            BrandName = BrandName,
            Price = new MoneyDto { Amount = PriceAmount, Currency = PriceCurrency },
            Status = Status,
            // Named arguments deliberately: three adjacent decimal? parameters would otherwise
            // transpose silently.
            Dimensions = ProductSearchViewMapper.ToDimensionsDto(
                length: DimensionsLength,
                width: DimensionsWidth,
                height: DimensionsHeight,
                unit: DimensionsUnit),
            Images = ToImageDtos(),
            CreatedAtUtc = CreatedAtUtc,
            LastUpdatedAtUtc = LastUpdatedAtUtc,
        };

    private List<ImageReferenceDto> ToImageDtos() =>
        ProductSearchViewMapper.DeserializeImages(ImagesJson)
            .Select(i => new ImageReferenceDto
            {
                Url = i.Url,
                AltText = i.AltText,
                DisplayOrder = i.DisplayOrder,
            })
            .ToList();
}
