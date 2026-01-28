using DotNetAtlas.SharedKernel.Base.DomainEvents;

namespace DotNetAtlas.SharedKernel.Base;

public interface IAggregateRoot
{
    void AddDomainEvent(DomainEvent domainEvent);
    IReadOnlyList<DomainEvent> PopDomainEvents();
}
