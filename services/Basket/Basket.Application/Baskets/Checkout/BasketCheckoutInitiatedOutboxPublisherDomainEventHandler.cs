using Basket.Application.Common.Data;
using Basket.Application.Common.Messaging;
using Basket.Domain.Baskets.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Application.Baskets.Checkout;

/// <summary>
/// In-process fan-out handler that transforms the internal
/// <see cref="BasketCheckedOutDomainEvent"/> into the external
/// <see cref="Basket.Sessions.BasketCheckoutInitiatedEvent"/> Avro record and
/// writes it to the transactional outbox.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the Weather reference pattern — see
/// <c>Weather.Application.WeatherFeedback.SendFeedback.FeedbackCreatedOutboxPublisherDomainEventHandler</c>.
/// The caller of <see cref="CheckoutBasketCommandHandler"/> owns the transaction
/// boundary; this handler only <b>adds</b> the outbox message. <c>SaveChangesAsync</c>
/// happens on the command-handler side.
/// </para>
/// <para>
/// The Kafka key is the user id as a string, matching the partition rule in
/// <c>events-catalog.md § 5.4</c> (all a user's checkouts share a partition so
/// the Checkout saga can correlate them by consumer-group affinity).
/// </para>
/// </remarks>
public sealed class BasketCheckoutInitiatedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<BasketCheckedOutDomainEvent>
{
    private readonly ITransactionalOutbox<IBasketDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<BasketCheckoutInitiatedOutboxPublisherDomainEventHandler> _logger;

    public BasketCheckoutInitiatedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IBasketDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<BasketCheckoutInitiatedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(BasketCheckedOutDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToBasketCheckoutInitiatedEvent();

        _outbox.AddOutboxMessage(
            _topics.BasketSessions,
            domainEvent.UserId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added BasketCheckoutInitiatedEvent to outbox. UserId: {UserId}, CorrelationId: {CorrelationId}, Items: {ItemCount}",
            domainEvent.UserId,
            domainEvent.CorrelationId,
            domainEvent.Snapshot.Items.Length);

        return Task.CompletedTask;
    }
}
