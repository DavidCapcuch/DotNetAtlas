using Catalog.Domain.Categories.Events;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Categories.CreateCategory;

/// <summary>
/// No-op projection handler for <see cref="CategoryCreatedDomainEvent"/>. <c>product_search_view</c>
/// has no per-category row; future work may pre-seed breadcrumb caches here. Kept as an explicit
/// <see cref="IDomainEventHandler{T}"/> so DI scanning stays uniform across events.
/// </summary>
public sealed class CategoryCreatedProjectionDomainEventHandler : IDomainEventHandler<CategoryCreatedDomainEvent>
{
    private readonly ILogger<CategoryCreatedProjectionDomainEventHandler> _logger;

    public CategoryCreatedProjectionDomainEventHandler(ILogger<CategoryCreatedProjectionDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(CategoryCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        _logger.LogDebug(
            "CategoryCreatedDomainEvent observed for {CategoryId} ({Name}); no projection update required in v1.",
            domainEvent.CategoryId, domainEvent.Name);
        return Task.CompletedTask;
    }
}
