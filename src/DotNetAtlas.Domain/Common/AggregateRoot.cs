using DotNetAtlas.Domain.Common.Events;

namespace DotNetAtlas.Domain.Common;

/// <summary>
/// Cluster of objects treated as a single unit.
/// Can contain entities, value objects, and other aggregates.
/// Enforce business rules (i.e. invariants)
/// Can be created externally.
/// Can raise domain events.
/// Represent a transactional boundary (i.e. all changes are saved or none are saved).
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : IComparable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> PopDomainEvents()
    {
        var copy = _domainEvents.ToList().AsReadOnly();
        _domainEvents.Clear();

        return copy;
    }
}
