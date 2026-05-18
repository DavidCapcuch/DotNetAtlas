using Platform.CQRS;

namespace Catalog.Application.Products.SearchProducts;

/// <summary>
/// Public query returning a paginated page of products. Text, category-prefix, price-range, and
/// status filters are all optional; discontinued products are hidden by default unless the
/// <c>catalog.show-discontinued-in-search</c> feature flag is enabled (ADR-0014).
/// </summary>
public sealed record SearchProductsQuery : IQuery<SearchProductsResponse>
{
    public string? Text { get; init; }

    public string? CategoryPathPrefix { get; init; }

    public decimal? MinPrice { get; init; }

    public decimal? MaxPrice { get; init; }

    public string? Currency { get; init; }

    public string? Status { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
