using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Domain.Alerts.Events;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.SharedKernel.Base.DomainEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weather.Alerts;

namespace DotNetAtlas.Application.WeatherAlerts.ExtendSubscription;

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

    public async Task Handle(SubscriptionExtendedDomainEvent domainEvent, CancellationToken ct)
    {
        var subscriptionExtendedEvent = domainEvent.ToSubscriptionExtendedEvent();

        // Key must be PaymentTransactionId to match saga correlation pattern
        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.WeatherAlertSubscriptions,
            domainEvent.PaymentTransactionId.ToString(),
            subscriptionExtendedEvent);
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Added SubscriptionExtendedEvent to outbox for extended subscription. " +
            "UserId: {UserId}, CorrelationId: {CorrelationId}, PaymentTransactionId: {PaymentTransactionId}, " +
            "ExtendedByDays: {ExtendedByDays}, NewExpiresAtUtc: {NewExpiresAtUtc}",
            domainEvent.UserId, domainEvent.CorrelationId, domainEvent.PaymentTransactionId,
            domainEvent.ExtendedByDays, domainEvent.NewExpiresAtUtc);
    }
}
