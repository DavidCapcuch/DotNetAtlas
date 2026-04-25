using Catalog.Domain.Categories.Events;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Categories.ReparentCategory;

/// <summary>
/// Projection-side observer for <see cref="CategoryReparentedDomainEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// The materialized <c>CategoryPath</c> column on every affected <c>product_search_view</c>
/// row is rewritten by <c>CategoryPathService.RewriteDescendantPathsAsync</c> inside the
/// reparent command handler — that bulk SQL update runs inside the
/// <c>Database.EnsureTransactionAsync</c> wrap so it commits (or rolls back) atomically with
/// the aggregate save, and is far cheaper than mutating each projection row individually.
/// </para>
/// <para>
/// This handler stays in the dispatcher so the seam remains visible to logs / traces
/// (a reparent fired through the projection pipeline). Recomputing
/// <c>CategoryBreadcrumb</c> across descendants is intentionally deferred — the column is
/// denormalized and rebuilding it requires walking the new path; not pedagogically central
/// to the CQRS-projection-on-Postgres story.
/// </para>
/// </remarks>
public sealed class CategoryReparentedProjectionHandler
    : IDomainEventHandler<CategoryReparentedDomainEvent>
{
    private readonly ILogger<CategoryReparentedProjectionHandler> _logger;

    public CategoryReparentedProjectionHandler(
        ILogger<CategoryReparentedProjectionHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(CategoryReparentedDomainEvent domainEvent, CancellationToken ct)
    {
        _logger.LogDebug(
            "CategoryReparentedDomainEvent for {CategoryId}: path cascade applied by CategoryPathService; " +
            "CategoryBreadcrumb on descendants may temporarily reflect the prior taxonomy.",
            domainEvent.CategoryId);
        return Task.CompletedTask;
    }
}
