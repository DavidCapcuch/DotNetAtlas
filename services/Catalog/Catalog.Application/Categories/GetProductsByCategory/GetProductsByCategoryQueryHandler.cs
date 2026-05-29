using Catalog.Application.Common.Data;
using Catalog.Application.Common.ReadModels;
using Catalog.Application.Products.SearchProducts;
using Catalog.Domain.Products.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Catalog.Application.Categories.GetProductsByCategory;

public sealed class GetProductsByCategoryQueryHandler
    : IQueryHandler<GetProductsByCategoryQuery, SearchProductsResponse>
{
    private readonly ICatalogDbContext _db;

    public GetProductsByCategoryQueryHandler(ICatalogDbContext db)
    {
        _db = db;
    }

    public async Task<Result<SearchProductsResponse>> HandleAsync(
        GetProductsByCategoryQuery query,
        CancellationToken ct)
    {
        IQueryable<ProductSearchViewRow> queryable = _db.ProductSearchView
            .AsNoTracking()
            .Where(r => r.Status == ProductStatus.Active.Name);

        if (query.IncludeDescendants)
        {
            var pathPrefix = await _db.Categories
                .AsNoTracking()
                .Where(c => c.Id == query.CategoryId)
                .TagWith(nameof(GetProductsByCategoryQueryHandler))
                .Select(c => c.Path.Value)
                .FirstOrDefaultAsync(ct);
            if (pathPrefix is null)
            {
                return Result.Ok(EmptyPage(query));
            }

            // Segment-bounded prefix match to avoid sibling leaks
            // (e.g. "/electronics" must not match "/electronics-toys").
            var pathPrefixWithSeparator = pathPrefix + "/";
            queryable = queryable.Where(r =>
                r.CategoryPath == pathPrefix || r.CategoryPath.StartsWith(pathPrefixWithSeparator));
        }
        else
        {
            queryable = queryable.Where(r => r.CategoryId == query.CategoryId);
        }

        var total = await queryable.CountAsync(ct);

        var rows = await queryable
            .OrderBy(r => r.Name).ThenBy(r => r.ProductId)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .TagWith(nameof(GetProductsByCategoryQueryHandler))
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

    private static SearchProductsResponse EmptyPage(GetProductsByCategoryQuery query) => new()
    {
        Total = 0,
        PageNumber = query.PageNumber,
        PageSize = query.PageSize,
        Items = Array.Empty<SearchProductsResultItem>(),
    };
}
