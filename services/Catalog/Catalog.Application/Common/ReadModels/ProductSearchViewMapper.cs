using System.Text.Json;
using Catalog.Application.Common.Contracts;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// JSON (de)serialization helpers for the <c>Dimensions</c> and <c>Images</c> JSONB columns of the
/// <c>product_search_view</c> projection, plus the read model's interpretation of them. The write
/// side serializes (used by the projection domain-event handler); the read-side projection rows
/// deserialize after the SQL projection.
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

    /// <summary>
    /// Which of a product's images is the primary one — lowest <c>DisplayOrder</c> wins, <c>null</c>
    /// when there are none. One rule for the whole read model: endpoints render the primary image,
    /// they don't each get to pick it. Images tying on <c>DisplayOrder</c> fall back to JSON array
    /// order.
    /// </summary>
    public static string? DeserializePrimaryImageUrl(string? json)
        => DeserializeImages(json).OrderBy(i => i.DisplayOrder).FirstOrDefault()?.Url;

    public static string SerializeImages(IReadOnlyCollection<ImageReferenceDto> images)
        => JsonSerializer.Serialize(images);

    public static string? SerializeDimensions(DimensionsDto? dimensions)
        => dimensions is null ? null : JsonSerializer.Serialize(dimensions);
}
