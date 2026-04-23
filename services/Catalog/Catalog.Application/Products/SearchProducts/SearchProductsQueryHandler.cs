using Catalog.Application.Common.Data;
using Catalog.Application.Common.FeatureFlags;
using Catalog.Application.Common.ReadModels;
using Catalog.Application.Products.CreateProduct;
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
        var includeDiscontinued = await _featureClient.GetBooleanValueAsync(
            CatalogFeatureFlags.ShowDiscontinuedInSearch,
            defaultValue: false,
            cancellationToken: ct);

        IQueryable<ProductSearchViewRow> queryable = _db.ProductSearchView.AsNoTracking();

        if (!string.IsNullOrEmpty(query.Status))
        {
            queryable = queryable.Where(r => r.Status == query.Status);
        }
        else if (!includeDiscontinued)
        {
            queryable = queryable.Where(r => r.Status == ProductStatus.Active.Name);
        }

        if (!string.IsNullOrEmpty(query.Text))
        {
            var pattern = query.Text;
            queryable = queryable.Where(r =>
                EF.Functions.Like(r.Name, $"%{pattern}%") ||
                EF.Functions.Like(r.Description, $"%{pattern}%"));
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

        if (query.MinPrice.HasValue)
        {
            queryable = queryable.Where(r =>
                r.PriceAmount >= query.MinPrice!.Value && r.PriceCurrency == query.Currency);
        }

        if (query.MaxPrice.HasValue)
        {
            queryable = queryable.Where(r =>
                r.PriceAmount <= query.MaxPrice!.Value && r.PriceCurrency == query.Currency);
        }

        var total = await queryable.CountAsync(ct);

        var rows = await queryable
            .OrderBy(r => r.PriceAmount).ThenBy(r => r.ProductId)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(ToResultItem).ToList();

        return Result.Ok(new SearchProductsResponse
        {
            Total = total,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            Items = items,
        });
    }

    private static SearchProductsResultItem ToResultItem(ProductSearchViewRow row)
    {
        var images = ProductSearchViewMapper.DeserializeImages(row.ImagesJson);
        var primaryUrl = images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.Url;

        return new SearchProductsResultItem
        {
            ProductId = row.ProductId,
            Sku = row.Sku,
            Name = row.Name,
            CategoryBreadcrumb = row.CategoryBreadcrumb,
            BrandName = row.BrandName,
            Price = new MoneyDto { Amount = row.PriceAmount, Currency = row.PriceCurrency },
            Status = row.Status,
            PrimaryImageUrl = primaryUrl,
        };
    }
}
