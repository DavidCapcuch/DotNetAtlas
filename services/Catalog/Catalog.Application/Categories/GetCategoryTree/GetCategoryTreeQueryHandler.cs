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
            rootPath = await _db.Categories
                .AsNoTracking()
                .Where(c => c.Id == query.RootCategoryId.Value)
                .TagWith($"{nameof(GetCategoryTreeQueryHandler)}:RootPath")
                .Select(c => c.Path.Value)
                .FirstOrDefaultAsync(ct);
            if (rootPath is null)
            {
                return Result.Ok(new GetCategoryTreeResponse { Nodes = Array.Empty<CategoryTreeNode>() });
            }
        }

        IQueryable<Category> categoryQuery = _db.Categories.AsNoTracking();
        if (rootPath is not null)
        {
            // Segment-bounded prefix match — include the root itself plus descendants, but
            // never siblings whose raw path shares a leading substring ("/electronics" must
            // not match "/electronics-toys").
            var rootPathWithSeparator = rootPath + "/";
            categoryQuery = categoryQuery.Where(c =>
                c.Path.Value == rootPath || c.Path.Value.StartsWith(rootPathWithSeparator));
        }

        var categories = await categoryQuery
            .OrderBy(c => c.Path.Value)
            .TagWith(nameof(GetCategoryTreeQueryHandler))
            .Select(c => new CategoryNodeRow(c.Id, c.Name, c.Path.Value, c.ParentCategoryId))
            .ToListAsync(ct);

        // CAT-RV-H06: without this filter the GROUP BY scanned every
        // product_search_view row on every call (O(catalog), full-table scan at scale).
        // Constrain the count query to the categories we just loaded — EF Core translates
        // HashSet<Guid>.Contains into a parameterised IN (...) clause.
        var loadedCategoryIds = categories.Select(c => c.Id).ToHashSet();
        var activeName = ProductStatus.Active.Name;
        var counts = loadedCategoryIds.Count == 0
            ? new List<KeyValuePair<Guid, int>>()
            : await _db.ProductSearchView
                .AsNoTracking()
                .Where(r => r.Status == activeName && loadedCategoryIds.Contains(r.CategoryId))
                .GroupBy(r => r.CategoryId)
                .Select(g => KeyValuePair.Create(g.Key, g.Count()))
                .TagWith($"{nameof(GetCategoryTreeQueryHandler)}:Count")
                .ToListAsync(ct);

        var countByCategoryId = counts.ToDictionary(x => x.Key, x => x.Value);

        var nodes = categories.Select(c => new CategoryTreeNode
        {
            CategoryId = c.Id,
            Name = c.Name,
            Path = c.Path,
            ParentCategoryId = c.ParentCategoryId,
            Depth = CountSegments(c.Path),
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

    /// <summary>SQL-side projection of the category columns the tree response needs.</summary>
    private sealed record CategoryNodeRow(Guid Id, string Name, string Path, Guid? ParentCategoryId);
}
