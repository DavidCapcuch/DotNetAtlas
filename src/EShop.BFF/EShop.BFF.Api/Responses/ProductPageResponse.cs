namespace EShop.BFF.Api.Responses;

/// <summary>
/// Public product-detail page (bff.md § 3.1) — Catalog product info composed with Inventory
/// availability. <c>InStock</c> / <c>AvailableQty</c> are <c>null</c> and <c>HasStaleData</c> is
/// <c>true</c> when Inventory was unavailable at composition time (the response still 200s; the
/// endpoint adds <c>X-BFF-PartialData: inventory</c>).
/// </summary>
public sealed record ProductPageResponse
{
    public required ProductDetailDto Product { get; init; }

    /// <summary><c>Available &gt; 0</c>; <c>null</c> when Inventory was unavailable.</summary>
    public bool? InStock { get; init; }

    /// <summary>Available quantity; <c>null</c> when Inventory was unavailable.</summary>
    public int? AvailableQty { get; init; }

    /// <summary><c>true</c> when any upstream datum is missing (here: Inventory unavailable).</summary>
    public required bool HasStaleData { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }
}

public sealed record ProductDetailDto
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string BrandName { get; init; }

    public required string CategoryBreadcrumb { get; init; }

    public required string CategoryPath { get; init; }

    public required MoneyDto Price { get; init; }

    public DimensionsDto? Dimensions { get; init; }

    public required IReadOnlyList<ProductImageDto> Images { get; init; }

    public required string Status { get; init; }
}

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record DimensionsDto(decimal Length, decimal Width, decimal Height, string Unit);

public sealed record ProductImageDto(string Url, string AltText, int DisplayOrder);
