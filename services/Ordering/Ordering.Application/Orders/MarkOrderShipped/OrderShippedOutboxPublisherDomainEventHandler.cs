using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.Orders.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Application.Orders.MarkOrderShipped;

public sealed class OrderShippedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<OrderShippedDomainEvent>
{
    private readonly ITransactionalOutbox<IOrderingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<OrderShippedOutboxPublisherDomainEventHandler> _logger;

    public OrderShippedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IOrderingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<OrderShippedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(OrderShippedDomainEvent domainEvent, CancellationToken ct)
    {
        var avro = domainEvent.ToOrderShippedEvent();

        _outbox.AddOutboxMessage(
            _topics.OrderingOrders,
            avro.OrderId.ToString(),
            avro);

        _logger.LogDebug("Queued OrderShippedEvent to outbox for Order {OrderId}", avro.OrderId);
        return Task.CompletedTask;
    }
}
