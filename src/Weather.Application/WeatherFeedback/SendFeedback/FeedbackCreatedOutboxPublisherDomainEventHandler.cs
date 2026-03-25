using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Weather.Application.Common.Data;
using Weather.Application.Common.Messaging;
using Weather.Domain.Feedback.Events;

namespace Weather.Application.WeatherFeedback.SendFeedback;

/// <summary>
/// Handles <see cref="FeedbackCreatedDomainEvent"/> by mapping it to an integration event
/// and adding it to the outbox for reliable publishing.
/// </summary>
public class
    FeedbackCreatedOutboxPublisherDomainEventHandler : IDomainEventHandler<FeedbackCreatedDomainEvent>
{
    private readonly ILogger<FeedbackCreatedOutboxPublisherDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public FeedbackCreatedOutboxPublisherDomainEventHandler(
        ILogger<FeedbackCreatedOutboxPublisherDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(FeedbackCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        var feedbackCreatedIntegrationEvent = domainEvent.ToFeedbackCreatedIntegrationEvent();

        _transactionalOutbox.AddOutboxMessage(
            _topicsOptions.WeatherFeedbackEvents,
            feedbackCreatedIntegrationEvent.FeedbackId.ToString(),
            feedbackCreatedIntegrationEvent);

        _logger.LogDebug(
            "Added FeedbackCreatedEvent to outbox for FeedbackId: {FeedbackId}",
            feedbackCreatedIntegrationEvent.FeedbackId);
    }
}
