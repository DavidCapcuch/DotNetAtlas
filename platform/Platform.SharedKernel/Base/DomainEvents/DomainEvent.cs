namespace Platform.SharedKernel.Base.DomainEvents;

public abstract record DomainEvent
{
    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}
