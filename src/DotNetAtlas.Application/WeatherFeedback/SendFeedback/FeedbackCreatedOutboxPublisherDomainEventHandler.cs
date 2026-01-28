using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Domain.Feedback.Events;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.SharedKernel.Base.DomainEvents;
using Microsoft.Extensions.Logging;

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

    public FeedbackCreatedOutboxPublisherDomainEventHandler(
        ILogger<FeedbackCreatedOutboxPublisherDomainEventHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter)
    {
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
    }

    public async Task Handle(FeedbackCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        var feedbackCreatedIntegrationEvent = domainEvent.ToFeedbackCreatedIntegrationEvent();

        _transactionalOutbox.AddOutboxMessage(
            feedbackCreatedIntegrationEvent.FeedbackId.ToString(), feedbackCreatedIntegrationEvent);

        // Save outbox messages immediately to ensure they're persisted even though
        // domain events are triggered within the same DbContext transaction. This provides
        // a safety net if the transaction handling behavior changes in the future.
        await _transactionalOutbox.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Added FeedbackCreatedEvent to outbox for FeedbackId: {FeedbackId}",
            domainEvent.FeedbackId);
    }
}
