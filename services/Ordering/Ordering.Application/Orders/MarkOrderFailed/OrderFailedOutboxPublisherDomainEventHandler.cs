using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.Orders.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Application.Orders.MarkOrderFailed;

public sealed class OrderFailedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<OrderFailedDomainEvent>
{
    private readonly ITransactionalOutbox<IOrderingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<OrderFailedOutboxPublisherDomainEventHandler> _logger;

    public OrderFailedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IOrderingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<OrderFailedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(OrderFailedDomainEvent domainEvent, CancellationToken ct)
    {
        var avro = domainEvent.ToOrderFailedEvent();

        _outbox.AddOutboxMessage(
            _topics.OrderingOrders,
            avro.OrderId.ToString(),
            avro);

        _logger.LogDebug(
            "Queued OrderFailedEvent to outbox for Order {OrderId} (AtStatus {AtStatus})",
            avro.OrderId, avro.AtStatus);

        return Task.CompletedTask;
    }
}
