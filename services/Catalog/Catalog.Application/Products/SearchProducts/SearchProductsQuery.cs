using Platform.CQRS;

namespace Catalog.Application.Products.SearchProducts;

/// <summary>
/// Public query returning a paginated page of products. Text, category-prefix, price-range, and
/// status filters are all optional; discontinued products are hidden by default unless the
/// <c>catalog.show-discontinued-in-search</c> feature flag is enabled (ADR-0014).
/// </summary>
public class SearchProductsQuery : IQuery<SearchProductsResponse>
{
    public string? Text { get; set; }

    public string? CategoryPathPrefix { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    public string? Currency { get; set; }

    public string? Status { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
