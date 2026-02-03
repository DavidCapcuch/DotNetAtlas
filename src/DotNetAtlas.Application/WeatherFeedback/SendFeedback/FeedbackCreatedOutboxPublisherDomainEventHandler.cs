using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Domain.Feedback.Events;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.SharedKernel.Base.DomainEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Application.WeatherFeedback.SendFeedback;

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

        // Save outbox messages immediately to ensure they're persisted even though
        // domain events are triggered within the same DbContext transaction. This provides
        // a safety net if the transaction handling behavior changes in the future.
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Added FeedbackCreatedEvent to outbox for FeedbackId: {FeedbackId}",
            domainEvent.FeedbackId);
    }
}
