using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.Orders.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Application.Orders.ConfirmOrder;

public sealed class OrderConfirmedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<OrderConfirmedDomainEvent>
{
    private readonly ITransactionalOutbox<IOrderingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<OrderConfirmedOutboxPublisherDomainEventHandler> _logger;

    public OrderConfirmedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IOrderingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<OrderConfirmedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(OrderConfirmedDomainEvent domainEvent, CancellationToken ct)
    {
        var avro = domainEvent.ToOrderConfirmedEvent();

        _outbox.AddOutboxMessage(
            _topics.OrderingOrders,
            avro.OrderId.ToString(),
            avro);

        _logger.LogDebug("Queued OrderConfirmedEvent to outbox for Order {OrderId}", avro.OrderId);
        return Task.CompletedTask;
    }
}
