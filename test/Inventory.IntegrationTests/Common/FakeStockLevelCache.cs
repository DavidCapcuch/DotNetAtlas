using System.Collections.Concurrent;
using Inventory.Application.StockItems.Common;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// In-memory <see cref="IStockLevelCache"/> for integration tests — keeps the suite
/// Redis-free (mirrors the <c>FakeOutboxWriter</c> swap). Faithfully reproduces the
/// production read-through semantics (per-id hits, single missing-factory call, misses not
/// cached) so handler logic is exercised, while exposing <see cref="Poison"/> /
/// <see cref="Contains"/> hooks the cache-behaviour tests need.
/// </summary>
internal sealed class FakeStockLevelCache : IStockLevelCache
{
    private readonly ConcurrentDictionary<Guid, StockLevelResponse> _store = new();

    public async Task<StockLevelResponse?> GetOrSetAsync(
        Guid productId,
        Func<CancellationToken, Task<StockLevelResponse?>> factory,
        CancellationToken ct)
    {
        if (_store.TryGetValue(productId, out var hit))
        {
            return hit;
        }

        var value = await factory(ct).ConfigureAwait(false);
        if (value is not null)
        {
            _store[productId] = value;
        }

        return value;
    }

    public async Task<IReadOnlyList<StockLevelResponse>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        Func<IReadOnlyCollection<Guid>, CancellationToken, Task<IReadOnlyList<StockLevelResponse>>> missingFactory,
        CancellationToken ct)
    {
        var found = new List<StockLevelResponse>(productIds.Count);
        var misses = new List<Guid>();

        foreach (var productId in productIds)
        {
            if (_store.TryGetValue(productId, out var hit))
            {
                found.Add(hit);
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
                _store[row.ProductId] = row;
                found.Add(row);
            }
        }

        return found;
    }

    public Task RemoveAsync(Guid productId, CancellationToken ct)
    {
        _store.TryRemove(productId, out _);
        return Task.CompletedTask;
    }

    /// <summary>Test hook — seed a (possibly stale/bogus) entry to prove a reader/command's cache reliance.</summary>
    public void Poison(StockLevelResponse value) => _store[value.ProductId] = value;

    /// <summary>Test hook — whether the key is currently cached.</summary>
    public bool Contains(Guid productId) => _store.ContainsKey(productId);

    /// <summary>Test hook — wipe between tests (parity with the DB reset).</summary>
    public void Clear() => _store.Clear();
}
