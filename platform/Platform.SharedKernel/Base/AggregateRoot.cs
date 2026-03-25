using Platform.SharedKernel.Base.DomainEvents;

namespace Platform.SharedKernel.Base;

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
    private readonly List<DomainEvent> _domainEvents = [];

    public void AddDomainEvent(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyList<DomainEvent> PopDomainEvents()
    {
        var copy = _domainEvents.ToList().AsReadOnly();
        _domainEvents.Clear();

        return copy;
    }
}
