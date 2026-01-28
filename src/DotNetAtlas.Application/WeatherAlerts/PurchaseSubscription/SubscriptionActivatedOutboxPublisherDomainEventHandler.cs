using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Domain.Alerts.Events;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.SharedKernel.Base.DomainEvents;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.PurchaseSubscription;

/// <summary>
/// Handles subscription-related domain events by publishing a <see cref="Weather.Alerts.SubscriptionActivatedEvent"/>
/// integration event to the outbox for the Saga Orchestrator.
/// </summary>
/// <remarks>
/// This handler responds to three domain events that all represent successful subscription activation:
/// <list type="bullet">
///   <item><see cref="SubscriberActivatedDomainEvent"/> - First-time paid subscriber</item>
///   <item><see cref="SubscriberReactivatedDomainEvent"/> - Returning subscriber after lapse</item>
///   <item><see cref="SubscriptionUpgradedDomainEvent"/> - Existing subscriber upgrading tier</item>
/// </list>
/// All three events result in the same integration event being published to notify the saga orchestrator
/// that subscription activation was successful.
/// </remarks>
public class SubscriptionActivatedOutboxPublisherDomainEventHandler :
    IDomainEventHandler<SubscriberActivatedDomainEvent>,
    IDomainEventHandler<SubscriberReactivatedDomainEvent>,
    IDomainEventHandler<SubscriptionUpgradedDomainEvent>
{
    private readonly ILogger<SubscriptionActivatedOutboxPublisherDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;

    public SubscriptionActivatedOutboxPublisherDomainEventHandler(
        ILogger<SubscriptionActivatedOutboxPublisherDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
    }

    public async Task Handle(SubscriberActivatedDomainEvent domainEvent, CancellationToken ct)
    {
        var subscriptionActivatedEvent = domainEvent.ToSubscriptionActivatedEvent();

        _transactionalOutbox.AddOutboxMessage(domainEvent.PaymentTransactionId.ToString(), subscriptionActivatedEvent);
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Added SubscriptionActivatedEvent to outbox for first-time subscriber. " +
            "UserId: {UserId}, PaymentTransactionId: {PaymentTransactionId}, Tier: {Tier}",
            domainEvent.UserId, domainEvent.PaymentTransactionId, domainEvent.Tier.Name);
    }

    public async Task Handle(SubscriberReactivatedDomainEvent domainEvent, CancellationToken ct)
    {
        var subscriptionActivatedEvent = domainEvent.ToSubscriptionActivatedEvent();

        _transactionalOutbox.AddOutboxMessage(domainEvent.PaymentTransactionId.ToString(), subscriptionActivatedEvent);
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Added SubscriptionActivatedEvent to outbox for reactivated subscriber. " +
            "UserId: {UserId}, PaymentTransactionId: {PaymentTransactionId}, Tier: {Tier}",
            domainEvent.UserId, domainEvent.PaymentTransactionId, domainEvent.Tier.Name);
    }

    public async Task Handle(SubscriptionUpgradedDomainEvent domainEvent, CancellationToken ct)
    {
        var subscriptionActivatedEvent = domainEvent.ToSubscriptionActivatedEvent();

        _transactionalOutbox.AddOutboxMessage(domainEvent.PaymentTransactionId.ToString(), subscriptionActivatedEvent);
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Added SubscriptionActivatedEvent to outbox for upgraded subscriber. " +
            "UserId: {UserId}, PaymentTransactionId: {PaymentTransactionId}, " +
            "PreviousTier: {PreviousTier}, NewTier: {NewTier}",
            domainEvent.UserId, domainEvent.PaymentTransactionId,
            domainEvent.PreviousTier.Name, domainEvent.NewTier.Name);
    }
}
