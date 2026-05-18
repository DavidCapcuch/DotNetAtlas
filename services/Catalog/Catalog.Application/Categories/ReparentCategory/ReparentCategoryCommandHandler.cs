using Catalog.Application.Categories.Common.Services;
using Catalog.Application.Common.Data;
using Catalog.Domain.Categories;
using Catalog.Domain.Categories.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Catalog.Application.Categories.ReparentCategory;

/// <summary>
/// Reparent command handler. Validates the category exists, the new parent (if given) exists,
/// rejects self-parenting, calls <see cref="ICategoryAncestryService"/> to reject descendant
/// cycles, and delegates to <see cref="Category.Reparent"/>.
/// </summary>
/// <remarks>
/// After a successful reparent, descendant categories' <c>Path</c> columns and the corresponding
/// <c>CategoryPath</c> in <c>product_search_view</c> are rewritten by
/// <see cref="ICategoryPathService"/>. The cascade and the aggregate save run inside a single
/// EF transaction (<c>EnsureTransactionAsync</c>) so a SaveChanges failure rolls back the bulk
/// updates as well.
/// </remarks>
public sealed class ReparentCategoryCommandHandler : ICommandHandler<ReparentCategoryCommand>
{
    private readonly ICatalogDbContext _db;
    private readonly ICategoryAncestryService _ancestry;
    private readonly ICategoryPathService _pathService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReparentCategoryCommandHandler> _logger;

    public ReparentCategoryCommandHandler(
        ICatalogDbContext db,
        ICategoryAncestryService ancestry,
        ICategoryPathService pathService,
        TimeProvider timeProvider,
        ILogger<ReparentCategoryCommandHandler> logger)
    {
        _db = db;
        _ancestry = ancestry;
        _pathService = pathService;
        _timeProvider = timeProvider;
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

            if (await _ancestry.WouldCreateCycleAsync(category.Id, newParentId, ct))
            {
                return Result.Fail(CategoryErrors.ReparentCreatesCycle(category.Id, newParentId));
            }
        }

        var oldPath = category.Path;
        var reparentResult = category.Reparent(
            command.NewParentCategoryId,
            newParent?.Path,
            _timeProvider.GetUtcNow());
        if (reparentResult.IsFailed)
        {
            return reparentResult;
        }

        await _db.Database.EnsureTransactionAsync(async () =>
        {
            await _pathService.RewriteDescendantPathsAsync(
                oldPath: oldPath.Value,
                newPath: category.Path.Value,
                excludedCategoryId: category.Id,
                ct);

            await _db.SaveChangesAsync(ct);

            // CAT-RV-H05 (Wave-1 closeout): RewriteDescendantPathsAsync issues a bulk
            // ExecuteUpdate that bypasses the change tracker, so any descendant Category
            // entities materialized in this scope hold the pre-update Path. Detach them
            // all so subsequent reads in the same scope re-fetch from the database.
            _db.ChangeTracker.Clear();
        }, ct);

        _logger.LogInformation(
            "Reparented Category {CategoryId} under {NewParentCategoryId}",
            command.CategoryId, command.NewParentCategoryId);

        return Result.Ok();
    }
}
