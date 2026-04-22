using Catalog.Domain.Categories.Events;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Categories.ReparentCategory;

/// <summary>
/// Projection handler for <see cref="CategoryReparentedDomainEvent"/> — no-op in M3.
/// Descendant path rewriting and <c>product_search_view.CategoryPath</c> / breadcrumb updates
/// ship with the deferred <c>CategoryPathService</c> cascade in a follow-up milestone.
/// </summary>
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
        _logger.LogWarning(
            "CategoryReparentedDomainEvent for {CategoryId}: descendant CategoryPath cascade is deferred to a post-M3 milestone; " +
            "existing product_search_view rows under the old path may be temporarily stale.",
            domainEvent.CategoryId);
        return Task.CompletedTask;
    }
}
