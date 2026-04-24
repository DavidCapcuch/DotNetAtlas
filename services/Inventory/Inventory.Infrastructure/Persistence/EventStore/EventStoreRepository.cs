using EntityFramework.Exceptions.Common;
using FluentResults;
using Inventory.Domain.StockItems;
using Inventory.Domain.StockItems.Errors;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Persistence.EventStore;

/// <summary>
/// Append-only repository for <see cref="StockItem"/> event streams persisted
/// in <c>inventory.stock_events</c>. Encapsulates the ES write cycle
/// described in <c>docs/bc-design/inventory.md</c> § 14.1: rehydrate, invoke
/// the caller's command against the aggregate, insert the emitted events at
/// the next contiguous versions. Optimistic concurrency is enforced by the
/// <c>PK(StreamId, Version)</c> — a conflict surfaces as
/// <see cref="UniqueConstraintException"/> from
/// <c>EntityFrameworkCore.Exceptions.PostgreSQL</c> and is retried once
/// (§ 10.2); a second conflict returns
/// <see cref="InventoryErrors.Concurrency"/>.
/// </summary>
public sealed class EventStoreRepository
{
    private const int MaxAttempts = 2;

    private readonly InventoryDbContext _ctx;

    public EventStoreRepository(InventoryDbContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Reads the full stream for <paramref name="streamId"/> in version order
    /// and folds it into a fresh <see cref="StockItem"/>. Returns an
    /// uninitialized aggregate (<c>Version=0</c>) when the stream has no rows.
    /// </summary>
    public async Task<StockItem> RehydrateAsync(Guid streamId, CancellationToken ct)
    {
        var rows = await _ctx.StockEvents
            .AsNoTracking()
            .Where(r => r.StreamId == streamId)
            .OrderBy(r => r.Version)
            .Select(r => new { r.EventType, r.Payload })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var events = rows.Select(r => StockEventSerializer.Deserialize(r.EventType, r.Payload));
        return StockItem.Fold(events);
    }

    /// <summary>
    /// Rehydrates the stream, invokes <paramref name="command"/> against the
    /// aggregate, and appends the emitted domain events at versions
    /// <c>current+1..current+N</c>. On a
    /// <see cref="UniqueConstraintException"/> (another writer appended first)
    /// the whole sequence is retried once against the now-current state;
    /// a second conflict returns
    /// <see cref="InventoryErrors.Concurrency"/>. A business-expected
    /// <see cref="Result.Fail(FluentResults.IError)"/> from
    /// <paramref name="command"/> short-circuits without touching the DB.
    /// </summary>
    /// <param name="streamId">The stream id (= <c>ProductId</c>).</param>
    /// <param name="command">
    /// Invoked with the rehydrated aggregate. Must return
    /// <see cref="Result.Ok"/> on success; any failure is returned to the
    /// caller unchanged.
    /// </param>
    /// <param name="correlationId">
    /// Saga correlation id stamped on every appended row (ADR-0008). Null for
    /// ops-originated writes.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<Result<StockItem>> AppendAsync(
        Guid streamId,
        Func<StockItem, Result> command,
        Guid? correlationId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // NOTE: the DbContext is configured with EnableRetryOnFailure (an
        // Npgsql execution strategy). That strategy retries only transient
        // errors — UniqueConstraintException (SQLSTATE 23505) is not one,
        // so it surfaces immediately and the loop below handles it.
        // If a future milestone adds an explicit BeginTransactionAsync here
        // (M4 wraps event append + projection upsert + outbox in one tx),
        // wrap the retry block in CreateExecutionStrategy().ExecuteAsync(...)
        // — EF rejects user-initiated transactions under a retrying strategy.

        // Captured from the first rehydrate so a ConcurrencyError reports
        // the version the caller originally expected to append at, not the
        // one it raced against on the second attempt.
        var originalExpectedVersion = 0;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var aggregate = await RehydrateAsync(streamId, ct).ConfigureAwait(false);
            var startVersion = aggregate.Version;
            if (attempt == 1)
            {
                originalExpectedVersion = startVersion + 1;
            }

            var commandResult = command(aggregate);
            if (commandResult.IsFailed)
            {
                // Business-expected failure (e.g. InsufficientStock). No rows
                // touched; surface the error unchanged.
                return commandResult.ToResult<StockItem>();
            }

            var events = aggregate.PopDomainEvents();
            if (events.Count == 0)
            {
                // Idempotent replay — command succeeded without emitting an
                // event (e.g. a duplicate confirm on an already-Confirmed
                // reservation). Nothing to persist.
                return Result.Ok(aggregate);
            }

            var addedRows = new List<StockEventRow>(events.Count);
            for (var i = 0; i < events.Count; i++)
            {
                var @event = events[i];
                var (eventType, payload) = StockEventSerializer.Serialize(@event);
                var row = StockEventRow.Create(
                    streamId: streamId,
                    version: startVersion + 1 + i,
                    eventType: eventType,
                    payload: payload,
                    occurredAtUtc: @event.OccurredOnUtc,
                    correlationId: correlationId);

                _ctx.StockEvents.Add(row);
                addedRows.Add(row);
            }

            try
            {
                await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result.Ok(aggregate);
            }
            catch (UniqueConstraintException)
            {
                // PK(StreamId, Version) collision — another writer appended
                // first. Detach our pending rows so the next rehydrate pulls
                // a clean state, then loop to retry.
                foreach (var row in addedRows)
                {
                    _ctx.Entry(row).State = EntityState.Detached;
                }
            }
        }

        return Result.Fail<StockItem>(InventoryErrors.Concurrency(streamId, originalExpectedVersion));
    }
}
