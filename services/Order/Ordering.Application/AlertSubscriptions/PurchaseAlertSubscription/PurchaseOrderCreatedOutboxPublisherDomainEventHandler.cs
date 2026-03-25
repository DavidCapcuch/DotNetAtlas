using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.AlertSubscriptionOrders.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Application.AlertSubscriptions.PurchaseAlertSubscription;

/// <summary>
/// Handles <see cref="AlertSubscriptionPurchaseOrderCreatedDomainEvent"/> by mapping it to an
/// <see cref="Order.AlertSubscriptions.AlertSubscriptionPurchaseInitiatedEvent"/> integration event
/// and adding it to the outbox for reliable publishing.
/// </summary>
public class PurchaseOrderCreatedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<AlertSubscriptionPurchaseOrderCreatedDomainEvent>
{
    private readonly ILogger<PurchaseOrderCreatedOutboxPublisherDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IOrderingDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public PurchaseOrderCreatedOutboxPublisherDomainEventHandler(
        ILogger<PurchaseOrderCreatedOutboxPublisherDomainEventHandler> logger,
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutbox;
        _topicsOptions = topicsOptions.Value;
    }

    public Task Handle(AlertSubscriptionPurchaseOrderCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        var integrationEvent = domainEvent.ToPurchaseInitiatedEvent();

        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.OrderAlertSubscriptions,
            domainEvent.AlertSubscriptionOrderId.ToString(),
            integrationEvent);

        _logger.LogDebug(
            "Added AlertSubscriptionPurchaseInitiatedEvent to outbox for AlertSubscriptionOrderId: {OrderId}",
            domainEvent.AlertSubscriptionOrderId);
        return Task.CompletedTask;
    }
}
