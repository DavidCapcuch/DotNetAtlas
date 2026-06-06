namespace Inventory.Application.StockItems.Common;

/// <summary>
/// Inventory-owned read-through cache fronting the <c>current_stock_levels</c>
/// projection for the two <b>display</b> queries — <c>GetStockLevelByProductIdQuery</c>
/// (single) and <c>GetStockLevelsBulkQuery</c> (batch, backs the BFF). See
/// <see href="../../../../docs/adr/0034-inventory-stock-availability-read-path.md">ADR-0034</see>.
/// </summary>
/// <remarks>
/// <para>
/// The cache key namespace (<c>inventory:stock:{ProductId}</c> on <c>redis-cache</c>)
/// and the serializer / TTL are Infrastructure details hidden behind this port — no
/// other service reads the keys. The reservation <i>decision</i> path
/// (<c>ReserveStockCommand</c> via the event-sourced aggregate) NEVER consults this
/// cache, so display staleness can never cause an oversell (oversell-safe by construction).
/// </para>
/// <para>
/// <see cref="RemoveAsync"/> is <b>best-effort</b>: <c>redis-cache</c> is volatile /
/// non-critical (ADR-0016), so a transient eviction failure is logged and swallowed —
/// it MUST NOT fail the stock-mutating transaction that triggered it. The short
/// fail-safe TTL bounds staleness until the next successful write or read-through refresh.
/// </para>
/// </remarks>
public interface IStockLevelCache
{
    /// <summary>
    /// Returns the cached stock level for <paramref name="productId"/>, or runs
    /// <paramref name="factory"/> (the projection read) on a miss and caches a non-null
    /// result. A <c>null</c> factory result (uninitialized / unknown product) is returned
    /// as-is and deliberately NOT cached, so a later <c>InitializeStockItem</c> is not
    /// shadowed by a cached 404.
    /// </summary>
    Task<StockLevelResponse?> GetOrSetAsync(
        Guid productId,
        Func<CancellationToken, Task<StockLevelResponse?>> factory,
        CancellationToken ct);

    /// <summary>
    /// Batch read-through: returns the cached levels for the supplied
    /// <paramref name="productIds"/> (per-id cache hits), invoking
    /// <paramref name="missingFactory"/> exactly once with the cache misses and caching
    /// each freshly-fetched row. The returned list contains only the products that exist
    /// (hits + factory rows); ids with no row anywhere are simply absent — the caller
    /// derives <c>MissingProductIds</c> by difference (partial-tolerant per ADR-0034).
    /// </summary>
    Task<IReadOnlyList<StockLevelResponse>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        Func<IReadOnlyCollection<Guid>, CancellationToken, Task<IReadOnlyList<StockLevelResponse>>> missingFactory,
        CancellationToken ct);

    /// <summary>
    /// Best-effort eviction of <c>inventory:stock:{productId}</c>. Called by the
    /// projection handler on every applied ES event so the next read rebuilds from the
    /// freshly-upserted projection row. Never throws on a transient cache failure
    /// (see the type-level remarks).
    /// </summary>
    Task RemoveAsync(Guid productId, CancellationToken ct);
}
