using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Weather.Application.Common.Data;
using Weather.Application.Common.Messaging;
using Weather.Domain.Alerts.Events;

namespace Weather.Application.WeatherAlerts.ProcessExpiredSubscriptions;

/// <summary>
/// Handles <see cref="SubscriptionDowngradedDomainEvent"/> by publishing a
/// <see cref="SendEmailNotificationCommand"/> to the outbox for the NotificationService.
/// </summary>
public class
    SubscriptionDowngradedSendDowngradedEmailDomainEventHandler : IDomainEventHandler<SubscriptionDowngradedDomainEvent>
{
    private const string SubscriptionDowngradedTemplateId = "weather-alerts.subscription-downgraded";

    private readonly ILogger<SubscriptionDowngradedSendDowngradedEmailDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public SubscriptionDowngradedSendDowngradedEmailDomainEventHandler(
        ILogger<SubscriptionDowngradedSendDowngradedEmailDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public Task Handle(SubscriptionDowngradedDomainEvent domainEvent, CancellationToken ct)
    {
        var sendSubscriptionDowngradedEmailNotificationCommand = new SendEmailNotificationCommand
        {
            UserId = domainEvent.UserId,
            TemplateId = SubscriptionDowngradedTemplateId,
            TemplateData = new Dictionary<string, string>
            {
                ["previousTier"] = domainEvent.PreviousTier.Name,
                ["expiredAt"] = domainEvent.ExpiredAtUtc.ToString("O"),
                ["subscriptionsRemoved"] = domainEvent.SubscriptionsRemoved.ToString()
            },
            IdempotencyKey = $"subscription-downgraded-{domainEvent.SubscriberId}-{domainEvent.ExpiredAtUtc:O}",
            OccurredOnUtc = domainEvent.OccurredOnUtc.UtcDateTime
        };

        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.NotificationCommands,
            domainEvent.UserId.ToString(),
            sendSubscriptionDowngradedEmailNotificationCommand);

        _logger.LogDebug(
            "Published SendEmailNotificationCommand to outbox for subscription downgrade. " +
            "UserId: {UserId}, SubscriberId: {SubscriberId}", domainEvent.UserId, domainEvent.SubscriberId);
        return Task.CompletedTask;
    }
}
