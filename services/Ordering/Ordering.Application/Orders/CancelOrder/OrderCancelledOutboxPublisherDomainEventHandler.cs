using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.Orders.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Application.Orders.CancelOrder;

public sealed class OrderCancelledOutboxPublisherDomainEventHandler
    : IDomainEventHandler<OrderCancelledDomainEvent>
{
    private readonly ITransactionalOutbox<IOrderingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<OrderCancelledOutboxPublisherDomainEventHandler> _logger;

    public OrderCancelledOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IOrderingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<OrderCancelledOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(OrderCancelledDomainEvent domainEvent, CancellationToken ct)
    {
        var avro = domainEvent.ToOrderCancelledEvent();

        _outbox.AddOutboxMessage(
            _topics.OrderingOrders,
            avro.OrderId.ToString(),
            avro);

        _logger.LogDebug(
            "Queued OrderCancelledEvent to outbox for Order {OrderId} (AtStatus {AtStatus})",
            avro.OrderId, avro.AtStatus);

        return Task.CompletedTask;
    }
}
