namespace DotNetAtlas.SharedKernel.Base.DomainEvents;

/// <summary>
/// Dispatches domain events to their handlers without requiring an aggregate root.
/// Use this for scenarios where domain events need to be raised from services or query handlers
/// rather than from entities during SaveChanges.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches a domain event to all registered handlers.
    /// </summary>
    /// <typeparam name="TEvent">The type of domain event.</typeparam>
    /// <param name="domainEvent">The domain event to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : DomainEvent;
}
