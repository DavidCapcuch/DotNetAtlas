using Catalog.Domain.Categories.Events;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Categories.ReparentCategory;

/// <summary>
/// Projection-side observer for <see cref="CategoryReparentedDomainEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both the materialized <c>CategoryPath</c> and <c>CategoryBreadcrumb</c> columns on every
/// affected <c>product_search_view</c> row are rewritten by
/// <c>CategoryPathService.RewriteDescendantPathsAsync</c> inside the reparent command handler.
/// Those bulk SQL updates run inside the <c>Database.EnsureTransactionAsync</c> wrap so they
/// commit (or roll back) atomically with the aggregate save, and are far cheaper than mutating
/// each projection row individually.
/// </para>
/// <para>
/// This handler stays in the dispatcher so the seam remains visible to logs / traces
/// (a reparent fired through the projection pipeline). Recomputing the breadcrumb across
/// descendants runs inside the same UoW (CAT-RV-H07) so the breadcrumb never lags the path.
/// </para>
/// </remarks>
public sealed class CategoryReparentedProjectionDomainEventHandler
    : IDomainEventHandler<CategoryReparentedDomainEvent>
{
    private readonly ILogger<CategoryReparentedProjectionDomainEventHandler> _logger;

    public CategoryReparentedProjectionDomainEventHandler(
        ILogger<CategoryReparentedProjectionDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(CategoryReparentedDomainEvent domainEvent, CancellationToken ct)
    {
        _logger.LogDebug(
            "CategoryReparentedDomainEvent for {CategoryId}: path + breadcrumb cascade applied " +
            "by CategoryPathService.",
            domainEvent.CategoryId);
        return Task.CompletedTask;
    }
}
