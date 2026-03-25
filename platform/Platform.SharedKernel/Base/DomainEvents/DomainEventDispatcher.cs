using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.SharedKernel.Base.DomainEvents;

/// <summary>
/// Dispatches domain events to registered handlers via dependency injection.
/// This enables raising domain events from services and query handlers
/// without requiring an aggregate root or EF SaveChanges interceptor.
/// </summary>
internal sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Type> HandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> WrapperTypeCache = new();

    private readonly IServiceProvider _serviceProvider;

    public DomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : DomainEvent
    {
        var domainEventType = domainEvent.GetType();
        var handlerType = HandlerTypeCache.GetOrAdd(
            domainEventType,
            t => typeof(IDomainEventHandler<>).MakeGenericType(t));

        var handlers = _serviceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            var handlerWrapper = HandlerWrapper.Create(handler, domainEventType);
            await handlerWrapper.HandleAsync(domainEvent, ct);
        }
    }

    private abstract class HandlerWrapper
    {
        public abstract Task HandleAsync(DomainEvent domainEvent, CancellationToken ct);

        public static HandlerWrapper Create(object handler, Type domainEventType)
        {
            var wrapperType = WrapperTypeCache.GetOrAdd(
                domainEventType,
                t => typeof(HandlerWrapper<>).MakeGenericType(t));

            return (HandlerWrapper)Activator.CreateInstance(wrapperType, handler)!;
        }
    }

    private sealed class HandlerWrapper<T>(object handler) : HandlerWrapper
        where T : DomainEvent
    {
        private readonly IDomainEventHandler<T> _handler = (IDomainEventHandler<T>)handler;

        public override Task HandleAsync(DomainEvent domainEvent, CancellationToken ct)
        {
            return _handler.Handle((T)domainEvent, ct);
        }
    }
}
