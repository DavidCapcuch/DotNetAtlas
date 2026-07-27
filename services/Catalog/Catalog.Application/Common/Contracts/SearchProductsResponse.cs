namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Paginated product-search page returned by the <c>SearchProducts</c> and
/// <c>SearchAdminProducts</c> endpoints. One envelope serving two endpoints is an outstanding
/// ADR-0037 violation, tracked in #354 and #355.
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
