using System.Text.Json;
using Catalog.Application.Products.CreateProduct;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// JSON (de)serialization helpers for the <c>Dimensions</c> and <c>Images</c> JSONB columns of the
/// <c>product_search_view</c> projection. The write side serializes (used by the projection
/// domain-event handler); the read-side projection rows deserialize after the SQL projection.
/// </summary>
internal static class ProductSearchViewMapper
{
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
