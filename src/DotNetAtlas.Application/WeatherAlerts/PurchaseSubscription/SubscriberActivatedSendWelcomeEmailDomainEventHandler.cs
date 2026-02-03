using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Domain.Alerts.Events;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.SharedKernel.Base.DomainEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Email;

namespace DotNetAtlas.Application.WeatherAlerts.PurchaseSubscription;

/// <summary>
/// Handles <see cref="SubscriberActivatedDomainEvent"/> by publishing a
/// <see cref="SendEmailNotificationCommand"/> to the outbox for the NotificationService.
/// Sends a welcome email to first-time paid subscribers.
/// </summary>
public class SubscriberActivatedSendWelcomeEmailDomainEventHandler : IDomainEventHandler<SubscriberActivatedDomainEvent>
{
    private const string SendWelcomeTemplateId = "weather-alerts.subscriber-activated";

    private readonly ILogger<SubscriberActivatedSendWelcomeEmailDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public SubscriberActivatedSendWelcomeEmailDomainEventHandler(
        ILogger<SubscriberActivatedSendWelcomeEmailDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public Task Handle(SubscriberActivatedDomainEvent domainEvent, CancellationToken ct)
    {
        var sendWelcomeEmailNotificationCommand = new SendEmailNotificationCommand
        {
            UserId = domainEvent.UserId,
            TemplateId = SendWelcomeTemplateId,
            TemplateData = new Dictionary<string, string>
            {
                ["tier"] = domainEvent.Tier.Name,
                ["expiresAt"] = domainEvent.ExpiresAtUtc.ToString("O")
            },
            IdempotencyKey = $"subscriber-activated-{domainEvent.SubscriberId}-{domainEvent.OccurredOnUtc:O}",
            OccurredOnUtc = domainEvent.OccurredOnUtc.UtcDateTime
        };

        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.NotificationCommands,
            domainEvent.UserId.ToString(),
            sendWelcomeEmailNotificationCommand);

        _logger.LogDebug(
            "Published SendEmailNotificationCommand to outbox for subscriber activation. " +
            "UserId: {UserId}, SubscriberId: {SubscriberId}, Tier: {Tier}",
            domainEvent.UserId, domainEvent.SubscriberId, domainEvent.Tier.Name);

        return Task.CompletedTask;
    }
}
