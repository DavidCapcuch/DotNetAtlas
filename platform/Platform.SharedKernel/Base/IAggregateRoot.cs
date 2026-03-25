using Platform.SharedKernel.Base.DomainEvents;

namespace Platform.SharedKernel.Base;

public interface IAggregateRoot
{
    void AddDomainEvent(DomainEvent domainEvent);
    IReadOnlyList<DomainEvent> PopDomainEvents();
}
