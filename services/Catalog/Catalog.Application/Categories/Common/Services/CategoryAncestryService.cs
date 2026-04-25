using Catalog.Application.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Categories.Common.Services;

/// <summary>
/// Default implementation of <see cref="ICategoryAncestryService"/> backed by the materialized
/// <c>Categories.Path</c> column. A cycle would form iff the candidate parent's path equals
/// the category's path or sits strictly beneath it.
/// </summary>
/// <remarks>
/// Uses the segment-bounded prefix form (<c>== prefix || StartsWith(prefix + "/")</c>)
/// established by the M3 H2 fix to avoid false positives across path siblings that
/// share a leading substring.
/// </remarks>
public sealed class CategoryAncestryService : ICategoryAncestryService
{
    private readonly ICatalogDbContext _db;

    public CategoryAncestryService(ICatalogDbContext db)
    {
        _db = db;
    }

    public async Task<bool> WouldCreateCycleAsync(
        Guid categoryId,
        Guid newParentCategoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId == newParentCategoryId)
        {
            return true;
        }

        var paths = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Id == categoryId || c.Id == newParentCategoryId)
            .Select(c => new { c.Id, c.Path })
            .ToListAsync(cancellationToken);

        var categoryPath = paths.FirstOrDefault(p => p.Id == categoryId)?.Path?.Value;
        var newParentPath = paths.FirstOrDefault(p => p.Id == newParentCategoryId)?.Path?.Value;
        if (categoryPath is null || newParentPath is null)
        {
            return false;
        }

        return newParentPath == categoryPath
            || newParentPath.StartsWith(categoryPath + "/", StringComparison.Ordinal);
    }
}
