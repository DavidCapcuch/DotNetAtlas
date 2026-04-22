using System.Text.Json;
using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.GetProductById;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// Maps <see cref="ProductSearchViewRow"/> projections to the response DTOs exposed by Catalog's
/// query handlers. Serialization helpers use <c>System.Text.Json</c> for the <c>Dimensions</c>
/// and <c>Images</c> JSONB columns.
/// </summary>
internal static class ProductSearchViewMapper
{
    public static GetProductByIdResponse ToDetailResponse(ProductSearchViewRow row)
    {
        return new GetProductByIdResponse
        {
            ProductId = row.ProductId,
            Sku = row.Sku,
            Name = row.Name,
            Description = row.Description,
            CategoryId = row.CategoryId,
            CategoryPath = row.CategoryPath,
            CategoryBreadcrumb = row.CategoryBreadcrumb,
            BrandName = row.BrandName,
            Price = new MoneyDto { Amount = row.PriceAmount, Currency = row.PriceCurrency },
            Status = row.Status,
            Dimensions = DeserializeDimensions(row.DimensionsJson),
            Images = DeserializeImages(row.ImagesJson),
            CreatedAtUtc = row.CreatedAtUtc,
            LastUpdatedAtUtc = row.LastUpdatedAtUtc,
        };
    }

    public static DimensionsDto? DeserializeDimensions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<DimensionsDto>(json);
    }

    public static IReadOnlyList<ImageReferenceDto> DeserializeImages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ImageReferenceDto>();
        }

        return JsonSerializer.Deserialize<List<ImageReferenceDto>>(json) ?? new List<ImageReferenceDto>();
    }

    public static string SerializeImages(IReadOnlyCollection<ImageReferenceDto> images)
        => JsonSerializer.Serialize(images);

    public static string? SerializeDimensions(DimensionsDto? dimensions)
        => dimensions is null ? null : JsonSerializer.Serialize(dimensions);
}
