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

        // Debug-level + "queued" verb: the row is only tracked in the EF change tracker
        // at this point. CheckoutBasketCommandHandler emits a separate
        // LogInformation("Published ...") after SaveChangesAsync succeeds so that
        // Splunk / Grafana dashboards counting "checkouts initiated" via the
        // information-level line never over-count on a transient SaveChanges failure.
        _logger.LogDebug(
            "Queued BasketCheckoutInitiatedEvent to outbox change-tracker. UserId: {UserId}, OrderId: {OrderId}, Items: {ItemCount}",
            domainEvent.UserId,
            domainEvent.OrderId,
            domainEvent.Snapshot.Items.Length);

        return Task.CompletedTask;
    }
}
