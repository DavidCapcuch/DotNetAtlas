using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Domain.Feedback.Events;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.SharedKernel.Base.DomainEvents;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherFeedback.ChangeFeedback;

/// <summary>
/// Handles <see cref="FeedbackChangedDomainEvent"/> by mapping it to an integration event
/// and adding it to the outbox for reliable publishing.
/// </summary>
public class FeedbackChangedOutboxPublisherDomainEventHandler : IDomainEventHandler<FeedbackChangedDomainEvent>
{
    private readonly ILogger<FeedbackChangedOutboxPublisherDomainEventHandler> _logger;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;

    public FeedbackChangedOutboxPublisherDomainEventHandler(
        ILogger<FeedbackChangedOutboxPublisherDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
    }

    public async Task Handle(FeedbackChangedDomainEvent domainEvent, CancellationToken ct)
    {
        var feedbackChangedIntegrationEvent = domainEvent.ToFeedbackChangedIntegrationEvent();

        _transactionalOutbox.AddOutboxMessage(
            feedbackChangedIntegrationEvent.FeedbackId.ToString(), feedbackChangedIntegrationEvent);
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Added FeedbackChangedEvent to outbox for FeedbackId: {FeedbackId}",
            domainEvent.FeedbackId);
    }
}
