using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Weather.Application.Common.Data;
using Weather.Application.Common.Messaging;
using Weather.Domain.Alerts.Events;

namespace Weather.Application.WeatherAlerts.PurchaseSubscription;

/// <summary>
/// Handles <see cref="SubscriberReactivatedDomainEvent"/> by publishing a
/// <see cref="SendEmailNotificationCommand"/> to the outbox for the NotificationService.
/// Sends a personalized "welcome back" email to returning subscribers.
/// </summary>
public class
    SubscriberReactivatedSendWelcomeBackEmailDomainEventHandler : IDomainEventHandler<SubscriberReactivatedDomainEvent>
{
    private const string WelcommeBackTemplateId = "weather-alerts.subscriber-reactivated";

    private readonly ILogger<SubscriberReactivatedSendWelcomeBackEmailDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public SubscriberReactivatedSendWelcomeBackEmailDomainEventHandler(
        ILogger<SubscriberReactivatedSendWelcomeBackEmailDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(SubscriberReactivatedDomainEvent domainEvent, CancellationToken ct)
    {
        var sendWelcomeBackEmailNotificationCommand = new SendEmailNotificationCommand
        {
            UserId = domainEvent.UserId,
            TemplateId = WelcommeBackTemplateId,
            TemplateData = new Dictionary<string, string>
            {
                ["tier"] = domainEvent.Tier.Name,
                ["expiresAt"] = domainEvent.ExpiresAtUtc.ToString("O"),
                ["previousSubscriptionExpiredAt"] = domainEvent.PreviousSubscriptionExpiredAtUtc.ToString("O")
            },
            IdempotencyKey = $"subscriber-reactivated-{domainEvent.SubscriberId}-{domainEvent.OccurredOnUtc:O}",
            OccurredOnUtc = domainEvent.OccurredOnUtc.UtcDateTime
        };

        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.NotificationCommands,
            domainEvent.UserId.ToString(),
            sendWelcomeBackEmailNotificationCommand);
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Published SendEmailNotificationCommand to outbox for subscriber reactivation. " +
            "UserId: {UserId}, SubscriberId: {SubscriberId}, Tier: {Tier}, PreviousExpiry: {PreviousExpiry}",
            domainEvent.UserId, domainEvent.SubscriberId, domainEvent.Tier.Name,
            domainEvent.PreviousSubscriptionExpiredAtUtc);
    }
}
