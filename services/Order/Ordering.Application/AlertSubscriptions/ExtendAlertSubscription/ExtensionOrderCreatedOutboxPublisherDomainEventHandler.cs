using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.AlertSubscriptionOrders.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Application.AlertSubscriptions.ExtendAlertSubscription;

/// <summary>
/// Handles <see cref="AlertSubscriptionExtensionOrderCreatedDomainEvent"/> by mapping it to an
/// <see cref="Order.AlertSubscriptions.AlertSubscriptionExtensionInitiatedEvent"/> integration event
/// and adding it to the outbox for reliable publishing.
/// </summary>
public class ExtensionOrderCreatedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<AlertSubscriptionExtensionOrderCreatedDomainEvent>
{
    private readonly ILogger<ExtensionOrderCreatedOutboxPublisherDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IOrderingDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public ExtensionOrderCreatedOutboxPublisherDomainEventHandler(
        ILogger<ExtensionOrderCreatedOutboxPublisherDomainEventHandler> logger,
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutbox;
        _topicsOptions = topicsOptions.Value;
    }

    public Task Handle(AlertSubscriptionExtensionOrderCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        var integrationEvent = domainEvent.ToExtensionInitiatedEvent();

        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.OrderAlertSubscriptions, domainEvent.AlertSubscriptionOrderId.ToString(), integrationEvent);

        _logger.LogDebug(
            "Added AlertSubscriptionExtensionInitiatedEvent to outbox for AlertSubscriptionOrderId: {OrderId}",
            domainEvent.AlertSubscriptionOrderId);
        return Task.CompletedTask;
    }
}
