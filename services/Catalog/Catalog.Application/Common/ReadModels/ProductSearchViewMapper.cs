using System.Text.Json;
using Catalog.Application.Common.Contracts;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// The read model's interpretation of the <c>product_search_view</c> columns that need one: the
/// <c>images_json</c> JSONB column (serialized by the projection domain-event handler, deserialized
/// by the projection rows after the SQL projection) and the flattened <c>dimensions_*</c> scalars.
/// </summary>
/// <remarks>
/// The two columns exit at deliberately different levels. <c>images_json</c> stops at
/// <see cref="ProductImageDocument"/> and leaves document-to-wire mapping to the consuming row,
/// because a serialized column has a stored contract worth isolating. The <c>dimensions_*</c>
/// scalars have no stored contract to protect, so they are read straight to
/// <see cref="Contracts.DimensionsDto"/> here rather than through a second type that would carry no
/// rule of its own.
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
    /// Reads the four <c>dimensions_*</c> columns as the optional <c>Dimensions</c> value object they
    /// mirror (see <see cref="ProductSearchViewRow.DimensionsLength"/> for the all-or-none rule, which
    /// a table <c>CHECK</c> enforces). A partial row is therefore unreachable; it reads as
    /// <c>null</c> — "dimensions unknown" — rather than throwing on a GET.
    /// </summary>
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
