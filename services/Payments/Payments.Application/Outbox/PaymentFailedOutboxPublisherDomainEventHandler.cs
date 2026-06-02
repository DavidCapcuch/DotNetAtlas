using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Domain.Transactions.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Outbox;

/// <summary>
/// Fan-out from <see cref="PaymentFailedDomainEvent"/> to the external Avro <c>PaymentFailedEvent</c>
/// on <c>payments.transactions</c>. Per ADR-0026 the Payments service owns all its lifecycle
/// integration events including the terminals — this publisher is symmetric with the
/// AuthorizationFailed / CaptureFailed handlers and replaces the former PaymentProcessingSaga-side
/// publication. Co-raised on both <see cref="PaymentAuthorizationFailedDomainEvent"/> (auth
/// decline) and <see cref="PaymentCaptureFailedDomainEvent"/> (capture decline); the Checkout saga
/// consumes the resulting <c>PaymentFailedEvent</c> to fast-fail on an authorization decline
/// rather than waiting out the payment timeout.
/// </summary>
public sealed class PaymentFailedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<PaymentFailedDomainEvent>
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<PaymentFailedOutboxPublisherDomainEventHandler> _logger;

    public PaymentFailedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<PaymentFailedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(PaymentFailedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToPaymentFailedEvent();

        _outbox.AddOutboxMessage(
            _topics.Transactions,
            domainEvent.CorrelationId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added PaymentFailedEvent to outbox. PaymentId: {PaymentId}, CorrelationId: {CorrelationId}, Reason: {Reason}",
            domainEvent.PaymentId,
            domainEvent.CorrelationId,
            domainEvent.FailureInfo.Reason.Name);

        return Task.CompletedTask;
    }
}
