using Catalog.Application.Common.Contracts;

namespace Catalog.Application.Categories.GetProductsByCategory;

/// <summary>
/// Published wire contract of the <c>GetProductsByCategory</c> endpoint, owned by this slice
/// (ADR-0037). Its shape coincides with the other product-listing endpoints today; they are
/// independent contracts free to diverge, not duplication awaiting extraction.
/// </summary>
public sealed record GetProductsByCategoryResponse
{
    public required int Total { get; init; }

    public required int PageNumber { get; init; }

    public required int PageSize { get; init; }

    public required IReadOnlyList<GetProductsByCategoryResultItem> Items { get; init; }
}

/// <summary>
/// One product summary in a <see cref="GetProductsByCategoryResponse"/> page. <see cref="Price"/>
/// stays the BC-wide <see cref="MoneyDto"/>: value DTOs are shared where envelopes are not, because
/// money has no endpoint-specific reason to change (ADR-0037 § Rationale — the volatility test).
/// </summary>
public sealed record GetProductsByCategoryResultItem
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string CategoryBreadcrumb { get; init; }

    public required string BrandName { get; init; }

    public required MoneyDto Price { get; init; }

    public required string Status { get; init; }

    /// <summary>Lowest-<c>DisplayOrder</c> image URL; <c>null</c> when the product has no images.</summary>
    public string? PrimaryImageUrl { get; init; }
}
