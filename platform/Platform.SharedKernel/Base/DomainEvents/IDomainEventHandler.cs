namespace Platform.SharedKernel.Base.DomainEvents;

public interface IDomainEventHandler<in T>
    where T : DomainEvent
{
    Task Handle(T domainEvent, CancellationToken ct);
}
