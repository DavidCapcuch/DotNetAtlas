using Inventory.Application.StockItems.Common;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Inventory.Infrastructure.Persistence.Caching;

/// <summary>
/// FusionCache-backed <see cref="IStockLevelCache"/> over <c>redis-cache</c> (ADR-0034).
/// L2-only (the distributed copy is the single shared truth — see the DI wiring), so a
/// projection-update eviction on the shared key is visible to every Inventory instance
/// without a backplane. Keys are namespaced <c>inventory:stock:{ProductId}</c> and hidden
/// behind the Inventory HTTP API — no other service reads them.
/// </summary>
internal sealed class FusionStockLevelCache : IStockLevelCache
{
    /// <summary>Named-cache id shared by the DI registration and the keyed multiplexer.</summary>
    internal const string CacheName = "inventory-stock";

    /// <summary>Cache-key namespace per ADR-0034 Implementation Notes.</summary>
    internal const string KeyPrefix = "inventory:stock:";

    private readonly IFusionCache _cache;
    private readonly ILogger<FusionStockLevelCache> _logger;

    public FusionStockLevelCache(IFusionCacheProvider cacheProvider, ILogger<FusionStockLevelCache> logger)
    {
        _cache = cacheProvider.GetCache(CacheName);
        _logger = logger;
    }

    public async Task<StockLevelResponse?> GetOrSetAsync(
        Guid productId,
        Func<CancellationToken, Task<StockLevelResponse?>> factory,
        CancellationToken ct)
    {
        var cached = await _cache.GetOrSetAsync<CachedStockLevel?>(
            Key(productId),
            async (ctx, token) =>
            {
                var value = await factory(token).ConfigureAwait(false);
                if (value is null)
                {
                    // Do not cache a miss — a later InitializeStockItem would otherwise be
                    // shadowed by a cached 404 until the TTL elapsed (ADR-0034).
                    ctx.Options.SetDuration(TimeSpan.Zero);
                    return null;
                }

                return ToCached(value);
            },
            token: ct).ConfigureAwait(false);

        return cached is null ? null : ToResponse(cached);
    }

    public async Task<IReadOnlyList<StockLevelResponse>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        Func<IReadOnlyCollection<Guid>, CancellationToken, Task<IReadOnlyList<StockLevelResponse>>> missingFactory,
        CancellationToken ct)
    {
        var found = new List<StockLevelResponse>(productIds.Count);
        var misses = new List<Guid>();

        // Per-id cache lookups (ADR-0034 Implementation Notes). StackExchange.Redis pipelines
        // the GETs over its multiplexer, so this is one network round trip's worth of latency,
        // not N — and bounded by the validator's 200-id cap.
        //
        // The batch path deliberately uses TryGet/Set rather than the single read's
        // GetOrSetAsync: it forgoes per-key fail-safe + stampede collapse because the query is
        // partial-tolerant — a corrupt/unreachable entry is treated as a miss (ReThrow* off, see
        // the DI wiring) and a missing projection row simply lands in MissingProductIds. So
        // degradation here is "reported missing / rebuilt", not "serve stale".
        foreach (var productId in productIds)
        {
            var maybe = await _cache.TryGetAsync<CachedStockLevel>(Key(productId), token: ct).ConfigureAwait(false);
            if (maybe.HasValue)
            {
                found.Add(ToResponse(maybe.Value));
            }
            else
            {
                misses.Add(productId);
            }
        }

        if (misses.Count > 0)
        {
            var fetched = await missingFactory(misses, ct).ConfigureAwait(false);
            foreach (var row in fetched)
            {
                await _cache.SetAsync(Key(row.ProductId), ToCached(row), token: ct).ConfigureAwait(false);
                found.Add(row);
            }
        }

        return found;
    }

    public async Task RemoveAsync(Guid productId, CancellationToken ct)
    {
        try
        {
            await _cache.RemoveAsync(Key(productId), token: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort per the IStockLevelCache contract — must not fail the triggering
            // transaction; the short TTL bounds staleness until the next write/read.
            _logger.LogWarning(
                ex,
                "Best-effort stock-level cache eviction failed for Product {ProductId}; short TTL will reclaim staleness.",
                productId);
        }
    }

    private static string Key(Guid productId) => $"{KeyPrefix}{productId:D}";

    private static CachedStockLevel ToCached(StockLevelResponse response) =>
        new(
            response.ProductId,
            response.OnHand,
            response.Reserved,
            response.Available,
            response.LastUpdatedUtc,
            response.LastVersion);

    private static StockLevelResponse ToResponse(CachedStockLevel cached) =>
        new()
        {
            ProductId = cached.ProductId,
            OnHand = cached.OnHand,
            Reserved = cached.Reserved,
            Available = cached.Available,
            LastUpdatedUtc = cached.LastUpdatedUtc,
            LastVersion = cached.LastVersion,
        };
}
