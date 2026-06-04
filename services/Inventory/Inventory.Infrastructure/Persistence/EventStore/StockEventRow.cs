namespace Inventory.Infrastructure.Persistence.EventStore;

/// <summary>
/// Event-store row persisted in <c>inventory.stock_events</c>. One row per
/// internal ES event. Composite PK <c>(StreamId, Version)</c> is the only
/// optimistic-concurrency mechanism (per <c>inventory.md § 10.1</c>).
/// </summary>
/// <remarks>
/// Internal on purpose — only the event-store repository builds and reads
/// these. Consumers work with <see cref="Platform.SharedKernel.Base.DomainEvents.DomainEvent"/>
/// instances rehydrated via <see cref="StockEventSerializer"/>.
/// </remarks>
internal sealed class StockEventRow
{
    // EF Core materialization ctor — parameterless + private so callers use Create.
    private StockEventRow()
    {
        EventType = null!;
        Payload = null!;
    }

    /// <summary>The stream identity = <c>ProductId</c>.</summary>
    public Guid StreamId { get; private set; }

    /// <summary>Monotonic 1-based per stream. Enforced by <c>PK(StreamId, Version)</c>.</summary>
    public int Version { get; private set; }

    /// <summary>The CLR-type discriminator (e.g. <c>"StockReservedDomainEvent"</c>) used by the deserializer.</summary>
    public string EventType { get; private set; }

    /// <summary>JSON-serialized event payload; stored in a <c>jsonb</c> column.</summary>
    public string Payload { get; private set; }

    /// <summary>UTC timestamp the domain event was produced. Promoted to a column for temporal queries.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>DB-side insert timestamp (<c>now()</c>); distinguishes domain time from persisted time during replay/tests.</summary>
    public DateTimeOffset AppendedAtUtc { get; private set; }

    /// <summary>Repository-only factory — the only way a row enters the write pipeline.</summary>
    internal static StockEventRow Create(
        Guid streamId,
        int version,
        string eventType,
        string payload,
        DateTimeOffset occurredAtUtc)
    {
        return new StockEventRow
        {
            StreamId = streamId,
            Version = version,
            EventType = eventType,
            Payload = payload,
            OccurredAtUtc = occurredAtUtc,

            // AppendedAtUtc is stamped DB-side via HasDefaultValueSql("now()");
            // the placeholder value here is never read.
        };
    }
}
