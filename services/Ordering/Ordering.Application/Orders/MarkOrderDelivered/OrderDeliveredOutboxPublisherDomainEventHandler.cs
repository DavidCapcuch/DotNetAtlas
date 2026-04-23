using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.Orders.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Application.Orders.MarkOrderDelivered;

public sealed class OrderDeliveredOutboxPublisherDomainEventHandler
    : IDomainEventHandler<OrderDeliveredDomainEvent>
{
    private readonly ITransactionalOutbox<IOrderingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<OrderDeliveredOutboxPublisherDomainEventHandler> _logger;

    public OrderDeliveredOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IOrderingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<OrderDeliveredOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(OrderDeliveredDomainEvent domainEvent, CancellationToken ct)
    {
        var avro = domainEvent.ToOrderDeliveredEvent();

        _outbox.AddOutboxMessage(
            _topics.OrderingOrders,
            avro.OrderId.ToString(),
            avro);

        _logger.LogDebug("Queued OrderDeliveredEvent to outbox for Order {OrderId}", avro.OrderId);
        return Task.CompletedTask;
    }
}
