using System.Text.Json;
using Catalog.Application.Common.Contracts;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// The read model's interpretation of the <c>product_search_view</c> columns that need one: the
/// <c>images_json</c> JSONB column (serialized by the projection domain-event handler) and the
/// flattened <c>dimensions_*</c> scalars. Both are read to their wire DTOs here, so a handler
/// mapping a projection row never parses a stored column itself.
/// </summary>
/// <remarks>
/// <c>images_json</c> additionally exits at <see cref="ProductImageDocument"/> via
/// <see cref="DeserializeImages"/>, because a serialized column has a stored contract worth
/// isolating from the wire shape — the two are free to differ. The <c>dimensions_*</c> scalars have
/// no stored contract to protect, so they have no such intermediate type.
/// </remarks>
internal static class ProductSearchViewMapper
{
    public static IReadOnlyList<ProductImageDocument> DeserializeImages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ProductImageDocument>();
        }

        return JsonSerializer.Deserialize<List<ProductImageDocument>>(json) ?? new List<ProductImageDocument>();
    }

    /// <summary>
    /// Which of a product's images is the primary one — lowest <c>DisplayOrder</c> wins, <c>null</c>
    /// when there are none. One rule for the whole read model: endpoints render the primary image,
    /// they don't each get to pick it. Images tying on <c>DisplayOrder</c> fall back to JSON array
    /// order.
    /// </summary>
    public static string? DeserializePrimaryImageUrl(string? json)
        => DeserializeImages(json).OrderBy(i => i.DisplayOrder).FirstOrDefault()?.Url;

    /// <summary>
    /// Reads the stored image documents as the wire <see cref="ImageReferenceDto"/> list, preserving
    /// stored order — unlike <see cref="DeserializePrimaryImageUrl"/>, which ranks by
    /// <c>DisplayOrder</c> to pick one. One home for the document-to-wire step so the slices reading
    /// full image detail cannot drift on it.
    /// </summary>
    public static IReadOnlyList<ImageReferenceDto> ToImageDtos(string? json)
        => DeserializeImages(json)
            .Select(i => new ImageReferenceDto
            {
                Url = i.Url,
                AltText = i.AltText,
                DisplayOrder = i.DisplayOrder,
            })
            .ToList();

    /// <summary>
    /// Reads the four <c>dimensions_*</c> columns as the optional <c>Dimensions</c> value object they
    /// mirror (see <see cref="ProductSearchViewRow.DimensionsLength"/> for the all-or-none rule, which
    /// a table <c>CHECK</c> enforces). A partial row is therefore unreachable; it reads as
    /// <c>null</c> — "dimensions unknown" — rather than throwing on a GET.
    /// </summary>
    /// <remarks>
    /// Call with named arguments: three adjacent <c>decimal?</c> parameters would otherwise transpose
    /// silently.
    /// </remarks>
    public static DimensionsDto? ToDimensionsDto(decimal? length, decimal? width, decimal? height, string? unit)
    {
        if (length is null || width is null || height is null || unit is null)
        {
            return null;
        }

        return new DimensionsDto
        {
            Length = length.Value,
            Width = width.Value,
            Height = height.Value,
            Unit = unit,
        };
    }

    public static string SerializeImages(IReadOnlyCollection<ProductImageDocument> images)
        => JsonSerializer.Serialize(images);
}
