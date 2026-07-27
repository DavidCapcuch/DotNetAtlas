using Catalog.Application.Common.Contracts;
using Catalog.Application.Common.Data;
using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Products.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Catalog.Application.Categories.GetProductsByCategory;

public sealed class GetProductsByCategoryQueryHandler
    : IQueryHandler<GetProductsByCategoryQuery, GetProductsByCategoryResponse>
{
    private readonly ICatalogDbContext _db;

    public GetProductsByCategoryQueryHandler(ICatalogDbContext db)
    {
        _db = db;
    }

    public async Task<Result<GetProductsByCategoryResponse>> HandleAsync(
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
                .TagWith($"{nameof(GetProductsByCategoryQueryHandler)}:CategoryPath")
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

        var total = await queryable
            .TagWith($"{nameof(GetProductsByCategoryQueryHandler)}:Count")
            .CountAsync(ct);

        var rows = await queryable
            .OrderBy(r => r.Name).ThenBy(r => r.ProductId)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .TagWith(nameof(GetProductsByCategoryQueryHandler))
            .Select(ProductSearchResultRow.Projection)
            .ToListAsync(ct);

        var items = rows.Select(ToResultItem).ToList();

        return Result.Ok(new GetProductsByCategoryResponse
        {
            Total = total,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            Items = items,
        });
    }

    private static GetProductsByCategoryResponse EmptyPage(GetProductsByCategoryQuery query) => new()
    {
        Total = 0,
        PageNumber = query.PageNumber,
        PageSize = query.PageSize,
        Items = Array.Empty<GetProductsByCategoryResultItem>(),
    };

    /// <summary>
    /// Lives here rather than on <see cref="ProductSearchResultRow"/> because that row is shared
    /// with <c>SearchProducts</c>, whose wire item is a separate type (ADR-0037) — one method
    /// cannot return both.
    /// </summary>
    private static GetProductsByCategoryResultItem ToResultItem(ProductSearchResultRow row) =>
        new()
        {
            ProductId = row.ProductId,
            Sku = row.Sku,
            Name = row.Name,
            CategoryBreadcrumb = row.CategoryBreadcrumb,
            BrandName = row.BrandName,
            Price = new MoneyDto { Amount = row.PriceAmount, Currency = row.PriceCurrency },
            Status = row.Status,
            PrimaryImageUrl = ProductSearchViewMapper.DeserializePrimaryImageUrl(row.ImagesJson),
        };
}
