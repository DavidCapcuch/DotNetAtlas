using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.Orders.Events;
using Ordering.Orders;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Application.Orders.CreateOrder;

/// <summary>
/// Publishes <see cref="OrderCreatedEvent"/> to the <c>ordering.orders</c>
/// Kafka topic via the transactional outbox. Runs inside the same EF-Core
/// transaction as the aggregate save (see the
/// <c>DispatchDomainEventsInterceptor</c> to be added in M4), so the outbox
/// row is atomic with the order row.
/// </summary>
public sealed class OrderCreatedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<OrderCreatedDomainEvent>
{
    private readonly ITransactionalOutbox<IOrderingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<OrderCreatedOutboxPublisherDomainEventHandler> _logger;

    public OrderCreatedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IOrderingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<OrderCreatedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(OrderCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        var avro = domainEvent.ToOrderCreatedEvent();

        _outbox.AddOutboxMessage(
            _topics.OrderingOrders,
            avro.OrderId.ToString(),
            avro);

        _logger.LogDebug(
            "Queued OrderCreatedEvent to outbox for Order {OrderId} on topic {Topic}",
            avro.OrderId, _topics.OrderingOrders);

        return Task.CompletedTask;
    }
}
