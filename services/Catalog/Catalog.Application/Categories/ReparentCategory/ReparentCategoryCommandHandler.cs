using Catalog.Application.Common.Data;
using Catalog.Domain.Categories;
using Catalog.Domain.Categories.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Catalog.Application.Categories.ReparentCategory;

/// <summary>
/// Reparent command handler. M3 scope: validates the category exists, the new parent (if given)
/// exists, rejects self-parenting, and delegates to <see cref="Category.Reparent"/>.
/// </summary>
/// <remarks>
/// <para>
/// TODO(M3-followup): run <c>CategoryAncestryService.WouldCreateCycle(...)</c> before calling
/// <c>category.Reparent(...)</c> so reparenting A under one of A's descendants is rejected with
/// <c>CategoryErrors.ReparentCreatesCycle</c>.
/// </para>
/// <para>
/// TODO(M3-followup): after a successful reparent, invoke <c>CategoryPathService</c> to rewrite
/// descendant categories' paths and the corresponding <c>CategoryPath</c> columns in
/// <c>product_search_view</c> — the descendant cascade is intentionally a no-op in M3 while the
/// necessary bulk-SQL infrastructure is still landing in M4.
/// </para>
/// </remarks>
public sealed class ReparentCategoryCommandHandler : ICommandHandler<ReparentCategoryCommand>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<ReparentCategoryCommandHandler> _logger;

    public ReparentCategoryCommandHandler(
        ICatalogDbContext db,
        ILogger<ReparentCategoryCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ReparentCategoryCommand command, CancellationToken ct)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == command.CategoryId, ct);
        if (category is null)
        {
            return Result.Fail(CategoryErrors.NotFound(command.CategoryId));
        }

        Category? newParent = null;
        if (command.NewParentCategoryId is { } newParentId)
        {
            newParent = await _db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == newParentId, ct);
            if (newParent is null)
            {
                return Result.Fail(CategoryErrors.NotFound(newParentId));
            }
        }

        var reparentResult = category.Reparent(command.NewParentCategoryId, newParent?.Path);
        if (reparentResult.IsFailed)
        {
            return reparentResult;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reparented Category {CategoryId} under {NewParentCategoryId}",
            command.CategoryId, command.NewParentCategoryId);

        return Result.Ok();
    }
}
