using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Base.DomainEvents;

namespace Weather.Infrastructure.Persistence.Database.Interceptors;

/// <summary>
/// Dispatches domain events to handlers before SaveChanges completes.
/// Handlers are resolved from the same DI scope as the DbContext,
/// ensuring they participate in the same transaction.
/// </summary>
public class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public DispatchDomainEventsInterceptor(IDomainEventDispatcher domainEventDispatcher)
    {
        _domainEventDispatcher = domainEventDispatcher;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is null)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var domainEvents = dbContext.ChangeTracker.Entries<IAggregateRoot>()
            .Select(entry => entry.Entity.PopDomainEvents())
            .SelectMany(de => de)
            .ToArray();

        foreach (var domainEvent in domainEvents)
        {
            // Dispatching domain events BEFORE base.SaveChanges,
            // allows handlers to add outbox messages in the same transaction
            // and therefore have guaranteed atomicity and message delivery.
            await _domainEventDispatcher.DispatchAsync(domainEvent, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
