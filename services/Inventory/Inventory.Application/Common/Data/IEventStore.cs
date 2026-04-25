using FluentResults;
using Inventory.Domain.StockItems;

namespace Inventory.Application.Common.Data;

/// <summary>
/// Application-layer port onto the event-sourced write path for
/// <see cref="StockItem"/> streams. Implemented in Infrastructure by
/// <c>EventStoreRepository</c>.
/// </summary>
/// <remarks>
/// Application handlers use this port (not a concrete repository type) so the
/// dependency direction remains <c>Infrastructure -&gt; Application</c>, never
/// the reverse.
/// </remarks>
public interface IEventStore
{
    /// <summary>
    /// Loads the full stream for <paramref name="streamId"/> and folds it into
    /// a <see cref="StockItem"/>. Returns an uninitialized aggregate
    /// (<c>Version=0</c>) when the stream has no rows.
    /// </summary>
    Task<StockItem> RehydrateAsync(Guid streamId, CancellationToken ct);

    /// <summary>
    /// Rehydrates the stream, invokes <paramref name="command"/> against the
    /// aggregate, and commits the emitted events + projection upserts +
    /// outbox writes in a single transaction. Retries once on optimistic
    /// concurrency conflict (<c>PK(StreamId, Version)</c> violation); a second
    /// conflict returns <c>InventoryErrors.Concurrency</c>.
    /// </summary>
    /// <param name="streamId">The stream id (= <c>ProductId</c>).</param>
    /// <param name="command">
    /// Pure function over the rehydrated aggregate. Must return
    /// <see cref="Result.Ok"/> on success; any business-expected failure
    /// (e.g. <c>InsufficientStockError</c>) is surfaced to the caller
    /// unchanged and the stream is not mutated.
    /// </param>
    /// <param name="correlationId">
    /// Saga correlation id stamped on every appended row (ADR-0008). Null
    /// for ops-originated writes.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<StockItem>> AppendAsync(
        Guid streamId,
        Func<StockItem, Result> command,
        Guid? correlationId,
        CancellationToken ct);
}
