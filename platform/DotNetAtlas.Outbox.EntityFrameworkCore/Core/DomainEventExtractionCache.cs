using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.Core;

/// <summary>
/// Registry for domain events extraction functions.
/// </summary>
public sealed class DomainEventExtractionCache
{
    private readonly ConcurrentDictionary<Type, Func<object, OutboxMessagesBatch>> _extractorsCache = new();
    private readonly ConcurrentDictionary<Type, Func<object, OutboxMessagesBatch>?> _runtimeTypeCache = new();
    private FrozenDictionary<Type, Func<object, OutboxMessagesBatch>>? _frozenExtractorsCache;

    /// <summary>
    /// Registers an event extractor for a specific entity type.
    /// Must be called before Build().
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="extractor">Function that extracts domain events from the entity.</param>
    internal void RegisterEventExtractor<TEntity>(Func<TEntity, OutboxMessagesBatch> extractor)
    {
        if (_frozenExtractorsCache != null)
        {
            throw new InvalidOperationException(
                "Cannot register extractors after Build() has been called.");
        }

        _extractorsCache[typeof(TEntity)] = entity => extractor((TEntity)entity);
    }

    /// <summary>
    /// Tries to extract aggregate data from an entity.
    /// Handles inheritance by checking if entity is assignable to registered types.
    /// Caches type resolutions for optimal performance.
    /// </summary>
    /// <param name="entity">The entity to extract from.</param>
    /// <param name="outboxMessagesBatch">The extracted aggregate data.</param>
    /// <returns>True if extraction was successful, false otherwise.</returns>
    internal bool TryExtract(object entity, out OutboxMessagesBatch outboxMessagesBatch)
    {
        var entityType = entity.GetType();

        if (_runtimeTypeCache.TryGetValue(entityType, out var cachedExtractor))
        {
            if (cachedExtractor != null)
            {
                outboxMessagesBatch = cachedExtractor(entity);
                return true;
            }

            outboxMessagesBatch = null!;
            return false;
        }

        if (_frozenExtractorsCache!.TryGetValue(entityType, out var extractor))
        {
            _runtimeTypeCache[entityType] = extractor;
            outboxMessagesBatch = extractor(entity);
            return true;
        }

        foreach (var (registeredType, registeredExtractor) in _frozenExtractorsCache)
        {
            if (registeredType.IsAssignableFrom(entityType))
            {
                _runtimeTypeCache[entityType] = registeredExtractor;
                outboxMessagesBatch = registeredExtractor(entity);
                return true;
            }
        }

        // Cache negative result to avoid repeated lookups
        _runtimeTypeCache[entityType] = null;
        outboxMessagesBatch = null!;

        return false;
    }

    /// <summary>
    /// Builds a frozen dictionary from registered extractors.
    /// </summary>
    internal void Build()
    {
        _frozenExtractorsCache = _extractorsCache.ToFrozenDictionary();
    }
}
