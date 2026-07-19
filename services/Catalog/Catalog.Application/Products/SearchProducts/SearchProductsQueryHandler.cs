using Catalog.Application.Common.Contracts;
using Catalog.Application.Common.Data;
using Catalog.Application.Common.FeatureFlags;
using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Products.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using OpenFeature;
using Platform.CQRS;

namespace Catalog.Application.Products.SearchProducts;

/// <summary>
/// Searches the <c>product_search_view</c> projection. The default result set hides
/// <c>Discontinued</c> products; the <c>catalog.show-discontinued-in-search</c> feature flag
/// (ADR-0014) unhides them at runtime without redeploy.
/// </summary>
public sealed class SearchProductsQueryHandler : IQueryHandler<SearchProductsQuery, SearchProductsResponse>
{
    private readonly ICatalogDbContext _db;
    private readonly IFeatureClient _featureClient;

    public SearchProductsQueryHandler(ICatalogDbContext db, IFeatureClient featureClient)
    {
        _db = db;
        _featureClient = featureClient;
    }

    public async Task<Result<SearchProductsResponse>> HandleAsync(SearchProductsQuery query, CancellationToken ct)
    {
        IQueryable<ProductSearchViewRow> queryable = _db.ProductSearchView.AsNoTracking();

        if (!string.IsNullOrEmpty(query.Status))
        {
            queryable = queryable.Where(r => r.Status == query.Status);
        }
        else if (!query.IncludeAllStatuses)
        {
            // #172: the admin endpoint sets IncludeAllStatuses = true and bypasses both this
            // default and the ADR-0014 feature flag, exposing Discontinued products without a
            // global toggle. Public callers stay at the default Active-only view, with the
            // feature flag still able to relax it for non-admin scenarios.
            var includeDiscontinued = await _featureClient.GetBooleanValueAsync(
                CatalogFeatureFlags.ShowDiscontinuedInSearch,
                defaultValue: false,
                cancellationToken: ct);

            if (!includeDiscontinued)
            {
                queryable = queryable.Where(r => r.Status == ProductStatus.Active.Name);
            }
        }

        if (!string.IsNullOrEmpty(query.Text))
        {
            // CAT-SEC-001 / CAT-RV-H03: escape LIKE metacharacters before substitution so a
            // user-supplied "%" or "_" cannot become a wildcard (or, worse, "%" alone widen the
            // pattern to a full-table scan). The validator caps Text length; this escape pinned
            // each surviving char to a literal match.
            var escaped = query.Text
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
            var pattern = $"%{escaped}%";
            queryable = queryable.Where(r =>
                EF.Functions.Like(r.Name, pattern, "\\") ||
                EF.Functions.Like(r.Description, pattern, "\\"));
        }

        if (!string.IsNullOrEmpty(query.CategoryPathPrefix))
        {
            // Segment-bounded prefix match: "/electronics" must match "/electronics" and
            // "/electronics/laptops" but NOT siblings like "/electronics-toys".
            var prefix = query.CategoryPathPrefix;
            var prefixWithSeparator = prefix + "/";
            queryable = queryable.Where(r =>
                r.CategoryPath == prefix || r.CategoryPath.StartsWith(prefixWithSeparator));
        }

        // CAT-RV-M02: lift the currency filter above the min/max branches —
        // when a price filter is present, the validator guarantees Currency is also present.
        if (query.MinPrice.HasValue || query.MaxPrice.HasValue)
        {
            var currency = query.Currency!;
            queryable = queryable.Where(r => r.PriceCurrency == currency);
        }

        if (query.MinPrice.HasValue)
        {
            queryable = queryable.Where(r => r.PriceAmount >= query.MinPrice!.Value);
        }

        if (query.MaxPrice.HasValue)
        {
            queryable = queryable.Where(r => r.PriceAmount <= query.MaxPrice!.Value);
        }

        var total = await queryable
            .TagWith($"{nameof(SearchProductsQueryHandler)}:Count")
            .CountAsync(ct);

        var rows = await queryable
            .OrderBy(r => r.PriceAmount).ThenBy(r => r.ProductId)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .TagWith(nameof(SearchProductsQueryHandler))
            .Select(ProductSearchResultRow.Projection)
            .ToListAsync(ct);

        var items = rows.Select(r => r.ToResultItem()).ToList();

        return Result.Ok(new SearchProductsResponse
        {
            Total = total,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            Items = items,
        });
    }
}
