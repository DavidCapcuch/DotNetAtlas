using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Domain.Feedback.Events;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.SharedKernel.Base.DomainEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Application.WeatherFeedback.ChangeFeedback;

/// <summary>
/// Handles <see cref="FeedbackChangedDomainEvent"/> by mapping it to an integration event
/// and adding it to the outbox for reliable publishing.
/// </summary>
public class FeedbackChangedOutboxPublisherDomainEventHandler : IDomainEventHandler<FeedbackChangedDomainEvent>
{
    private readonly ILogger<FeedbackChangedOutboxPublisherDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public FeedbackChangedOutboxPublisherDomainEventHandler(
        ILogger<FeedbackChangedOutboxPublisherDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(FeedbackChangedDomainEvent domainEvent, CancellationToken ct)
    {
        var feedbackChangedIntegrationEvent = domainEvent.ToFeedbackChangedIntegrationEvent();

        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.WeatherFeedbackEvents,
            feedbackChangedIntegrationEvent.FeedbackId.ToString(),
            feedbackChangedIntegrationEvent);
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Added FeedbackChangedEvent to outbox for FeedbackId: {FeedbackId}",
            domainEvent.FeedbackId);
    }
}
