using DotNetAtlas.Domain.Common.Events;

namespace DotNetAtlas.Domain.Common;

public interface IAggregateRoot
{
    void RaiseDomainEvent(IDomainEvent domainEvent);
    IReadOnlyList<IDomainEvent> PopDomainEvents();
}
