using Catalog.Application.Products.CreateProduct;

namespace Catalog.Application.Products.SearchProducts;

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
