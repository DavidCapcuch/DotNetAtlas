using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Domain.Alerts.Events;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.SharedKernel.Base.DomainEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Email;

namespace DotNetAtlas.Application.WeatherAlerts.ProcessExpiredSubscriptions;

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

    public async Task Handle(SubscriptionDowngradedDomainEvent domainEvent, CancellationToken ct)
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
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Published SendEmailNotificationCommand to outbox for subscription downgrade. " +
            "UserId: {UserId}, SubscriberId: {SubscriberId}", domainEvent.UserId, domainEvent.SubscriberId);
    }
}
