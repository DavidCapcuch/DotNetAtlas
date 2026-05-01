using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Infrastructure.Persistence.Database.Interceptors;

/// <summary>
/// Dispatches domain events from aggregate roots to handlers BEFORE
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)" /> commits. Handlers resolve from
/// the same DI scope as the DbContext, so outbox publishers enrolled as handlers write in the
/// same transaction as the aggregate save (reliable messaging guarantee).
/// </summary>
internal sealed class DispatchDomainEventsInterceptor : SaveChangesInterceptor
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
            await _domainEventDispatcher.DispatchAsync(domainEvent, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
