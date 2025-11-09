using System.Collections.Concurrent;
using System.Collections.Frozen;
using Avro.Specific;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.Core;

/// <summary>
/// Registry for cache functions which convert domain events to Avro representation.
/// </summary>
internal sealed class AvroMappingCache
{
    private readonly Dictionary<Type, object> _registeredMappers = [];
    private readonly ConcurrentDictionary<Type, AvroMapperWrapper?> _runtimeTypeMapperCache = new();
    private FrozenDictionary<Type, AvroMapperWrapper>? _frozenMapperCache;

    public void RegisterAvroMapper<TEvent>(Func<TEvent, ISpecificRecord> mapper)
    {
        if (_frozenMapperCache != null)
        {
            throw new InvalidOperationException(
                "Cannot register mappers after Build() has been called.");
        }

        _registeredMappers[typeof(TEvent)] = mapper;
    }

    /// <summary>
    /// Builds a frozen dictionary cache from registered mappings.
    /// </summary>
    internal void Build()
    {
        var cache = new Dictionary<Type, AvroMapperWrapper>();

        foreach (var (registeredType, mapper) in _registeredMappers)
        {
            var wrapper = AvroMapperWrapper.Create(mapper, registeredType);
            cache[registeredType] = wrapper;
        }

        _frozenMapperCache = cache.ToFrozenDictionary();
    }

    internal ISpecificRecord? MapToAvro(object domainEvent)
    {
        var eventType = domainEvent.GetType();
        if (_runtimeTypeMapperCache.TryGetValue(eventType, out var cachedAvroMapper))
        {
            return cachedAvroMapper?.Map(domainEvent);
        }

        if (_frozenMapperCache!.TryGetValue(eventType, out var cachedAvroMapperFromFrozen))
        {
            _runtimeTypeMapperCache[eventType] = cachedAvroMapperFromFrozen;

            return cachedAvroMapperFromFrozen.Map(domainEvent);
        }

        foreach (var (registeredType, registeredAvroMapper) in _frozenMapperCache)
        {
            if (registeredType.IsAssignableFrom(eventType))
            {
                _runtimeTypeMapperCache[eventType] = registeredAvroMapper;

                return registeredAvroMapper.Map(domainEvent);
            }
        }

        _runtimeTypeMapperCache[eventType] = null;

        return null;
    }

    private abstract class AvroMapperWrapper
    {
        public abstract ISpecificRecord? Map(object domainEvent);

        public static AvroMapperWrapper Create(object mapper, Type eventType)
        {
            var mapperForType = typeof(TypedAvroMapper<>).MakeGenericType(eventType);

            return (AvroMapperWrapper)Activator.CreateInstance(mapperForType, mapper)!;
        }
    }

    private sealed class TypedAvroMapper<T> : AvroMapperWrapper
    {
        private readonly Func<T, ISpecificRecord> _mapper;

        public TypedAvroMapper(object mapper)
        {
            _mapper = (Func<T, ISpecificRecord>)mapper;
        }

        public override ISpecificRecord Map(object domainEvent)
        {
            return _mapper((T)domainEvent);
        }
    }
}
