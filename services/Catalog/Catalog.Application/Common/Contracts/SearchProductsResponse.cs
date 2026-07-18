namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Paginated product-search page returned by the <c>SearchProducts</c> slice and reused verbatim by
/// <c>GetProductsByCategory</c> (which projects the same view). Lives in <c>Common.Contracts</c> so
/// neither slice owns a type the other depends on.
/// </summary>
public sealed class SearchProductsResponse
{
    public required int Total { get; set; }

    public required int PageNumber { get; set; }

    public required int PageSize { get; set; }

    public required IReadOnlyList<SearchProductsResultItem> Items { get; set; }
}

public sealed class SearchProductsResultItem
{
    public required Guid ProductId { get; set; }

    public required string Sku { get; set; }

    public required string Name { get; set; }

    public required string CategoryBreadcrumb { get; set; }

    public required string BrandName { get; set; }

    public required MoneyDto Price { get; set; }

    public required string Status { get; set; }

    public string? PrimaryImageUrl { get; set; }
}
