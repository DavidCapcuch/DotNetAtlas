using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Infrastructure.Persistence.Database.Interceptors;

/// <summary>
/// Dispatches domain events from <see cref="IAggregateRoot"/> instances tracked by the
/// DbContext to their handlers BEFORE <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
/// commits. Outbox publisher domain-event handlers therefore enqueue Avro outbox rows in the
/// same EF transaction as the aggregate save — the reliable-messaging guarantee for the
/// <c>InvoiceIssuedEvent</c> / <c>InvoiceCancelledEvent</c> / <c>CreditNoteIssuedEvent</c> trio.
/// </summary>
/// <remarks>
/// The dispatcher resolves handlers from the same DI scope as the DbContext, so any
/// <c>ITransactionalOutbox</c> write inside a handler joins the active transaction without
/// explicit coordination.
/// </remarks>
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
            .SelectMany(events => events)
            .ToArray();

        foreach (var domainEvent in domainEvents)
        {
            await _domainEventDispatcher.DispatchAsync(domainEvent, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
