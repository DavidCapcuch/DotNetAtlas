using Microsoft.EntityFrameworkCore;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Shared helper for the four enrichment consumers — absorbs the
/// find-by-correlation-id-or-insert primitive so each handler stays focused on
/// the per-event field-mapping + convergence logic.
/// </summary>
/// <remarks>
/// Intentionally thin: the rest of the upsert (which fields to write, how to
/// detect convergence, what now-stamp to apply) varies per handler and is
/// expressed inline in the calling code rather than threaded through this
/// helper. The example design decision in <c>invoicing.md:209</c> recommends
/// extracting common upsert logic; this is that extraction's minimum viable
/// shape — anything more would obscure the per-event semantics that future
/// readers need to see in one screen.
/// </remarks>
internal static class PendingProjectionUpsertHelper
{
    /// <summary>
    /// Find an existing row by correlation id; if absent, build via the
    /// factory and attach it to the change tracker. Caller owns
    /// <c>SaveChangesAsync</c>.
    /// </summary>
    /// <param name="set">DbSet of the projection table to search.</param>
    /// <param name="correlationId">Primary key of the projection row.</param>
    /// <param name="factory">Builds the row when none exists. Caller must populate the PK + FirstSeenAtUtc + this-half's columns.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>IsNew = true</c> when the factory ran (a new row was attached); <c>false</c> when the existing row was returned.</returns>
    public static async Task<(TRow Row, bool IsNew)> GetOrAddAsync<TRow>(
        DbSet<TRow> set,
        Guid correlationId,
        Func<TRow> factory,
        CancellationToken ct)
        where TRow : class
    {
        var existing = await set.FindAsync([correlationId], ct);
        if (existing is not null)
        {
            return (existing, false);
        }

        var newRow = factory();
        await set.AddAsync(newRow, ct);
        return (newRow, true);
    }
}
