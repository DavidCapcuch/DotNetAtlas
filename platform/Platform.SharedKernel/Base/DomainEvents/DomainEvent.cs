namespace Platform.SharedKernel.Base.DomainEvents;

public abstract record DomainEvent
{
    /// <summary>
    /// UTC timestamp when the event occurred. Callers must supply
    /// <c>TimeProvider.GetUtcNow()</c> per ADR-0015 — no wall-clock fallback.
    /// </summary>
    public required DateTimeOffset OccurredOnUtc { get; init; }
}
