using Platform.CQRS;

namespace Catalog.Application.Products.SearchProducts;

/// <summary>
/// Public query returning a paginated page of products. Text, category-prefix, price-range, and
/// status filters are all optional; discontinued products are hidden by default unless the
/// <c>catalog.show-discontinued-in-search</c> feature flag is enabled (ADR-0014).
/// </summary>
/// <remarks>
/// Setting <see cref="IncludeAllStatuses"/> to <c>true</c> bypasses both the default Active-only
/// filter and the feature-flag check. Reserved for the admin search endpoint
/// (<c>/api/v1/catalog/admin/products</c>) which is gated behind <c>catalog.write</c> scope per
/// #172 — public callers must not be able to set it (the public endpoint hard-codes <c>false</c>).
/// </remarks>
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

    public bool IncludeAllStatuses { get; init; }
}
