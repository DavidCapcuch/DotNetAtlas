using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Weather.Alerts;
using Weather.Application.Common.Data;
using Weather.Application.Common.Messaging;
using Weather.Domain.Alerts.Events;

namespace Weather.Application.WeatherAlerts.ExtendSubscription;

/// <summary>
/// Handles <see cref="SubscriptionExtendedDomainEvent"/> by publishing a <see cref="AlertSubscriptionExtendedEvent"/>
/// integration event to the outbox for the Extension Saga Orchestrator.
/// </summary>
/// <remarks>
/// This handler publishes an integration event when a subscription is successfully extended,
/// allowing the Extension Saga to complete its workflow.
/// The Kafka key is set to <see cref="SubscriptionExtendedDomainEvent.PaymentTransactionId"/>
/// which the saga uses for event correlation.
/// </remarks>
public class SubscriptionExtendedOutboxPublisherDomainEventHandler :
    IDomainEventHandler<SubscriptionExtendedDomainEvent>
{
    private readonly ILogger<SubscriptionExtendedOutboxPublisherDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public SubscriptionExtendedOutboxPublisherDomainEventHandler(
        ILogger<SubscriptionExtendedOutboxPublisherDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public Task Handle(SubscriptionExtendedDomainEvent domainEvent, CancellationToken ct)
    {
        var subscriptionExtendedEvent = domainEvent.ToSubscriptionExtendedEvent();

        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.WeatherAlertSubscriptions,
            domainEvent.PaymentTransactionId.ToString(),
            subscriptionExtendedEvent);

        _logger.LogDebug(
            "Added SubscriptionExtendedEvent to outbox for extended subscription. " +
            "UserId: {UserId}, CorrelationId: {CorrelationId}, PaymentTransactionId: {PaymentTransactionId}, " +
            "ExtendedByDays: {ExtendedByDays}, NewExpiresAtUtc: {NewExpiresAtUtc}",
            domainEvent.UserId, domainEvent.CorrelationId, domainEvent.PaymentTransactionId,
            domainEvent.ExtendedByDays, domainEvent.NewExpiresAtUtc);
        return Task.CompletedTask;
    }
}
