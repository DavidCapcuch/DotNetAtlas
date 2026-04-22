using Catalog.Application.Common.Data;
using Catalog.Domain.Categories;
using Catalog.Domain.Products.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Catalog.Application.Categories.GetCategoryTree;

public sealed class GetCategoryTreeQueryHandler
    : IQueryHandler<GetCategoryTreeQuery, GetCategoryTreeResponse>
{
    private readonly ICatalogDbContext _db;

    public GetCategoryTreeQueryHandler(ICatalogDbContext db)
    {
        _db = db;
    }

    public async Task<Result<GetCategoryTreeResponse>> HandleAsync(
        GetCategoryTreeQuery query,
        CancellationToken ct)
    {
        string? rootPath = null;
        if (query.RootCategoryId.HasValue)
        {
            var root = await _db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == query.RootCategoryId.Value, ct);
            if (root is null)
            {
                return Result.Ok(new GetCategoryTreeResponse { Nodes = Array.Empty<CategoryTreeNode>() });
            }

            rootPath = root.Path.Value;
        }

        IQueryable<Category> categoryQuery = _db.Categories.AsNoTracking();
        if (rootPath is not null)
        {
            categoryQuery = categoryQuery.Where(c => c.Path.Value.StartsWith(rootPath));
        }

        var categories = await categoryQuery
            .OrderBy(c => c.Path.Value)
            .ToListAsync(ct);

        var activeName = ProductStatus.Active.Name;
        var counts = await _db.ProductSearchView
            .AsNoTracking()
            .Where(r => r.Status == activeName)
            .GroupBy(r => r.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countByCategoryId = counts.ToDictionary(x => x.CategoryId, x => x.Count);

        var nodes = categories.Select(c => new CategoryTreeNode
        {
            CategoryId = c.Id,
            Name = c.Name,
            Path = c.Path.Value,
            ParentCategoryId = c.ParentCategoryId,
            Depth = CountSegments(c.Path.Value),
            ProductCount = countByCategoryId.GetValueOrDefault(c.Id),
        }).ToList();

        return Result.Ok(new GetCategoryTreeResponse { Nodes = nodes });
    }

    private static int CountSegments(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return 0;
        }

        // Materialized path shape: "/a/b/c" -> depth 3 (three segments).
        return path.Count(c => c == '/');
    }
}
