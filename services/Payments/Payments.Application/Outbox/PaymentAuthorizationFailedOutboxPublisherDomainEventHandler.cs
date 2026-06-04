using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Domain.Transactions.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Outbox;

/// <summary>
/// Fan-out from <see cref="PaymentAuthorizationFailedDomainEvent"/> to the external Avro event
/// on <c>payments.transactions</c>. Co-emitted with the in-process
/// <see cref="PaymentFailedDomainEvent"/>, which has no Payments-side outbox publisher —
/// <c>PaymentFailedEvent</c> is produced by PaymentProcessingSaga (events-catalog.md § 2).
/// </summary>
public sealed class PaymentAuthorizationFailedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<PaymentAuthorizationFailedDomainEvent>
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<PaymentAuthorizationFailedOutboxPublisherDomainEventHandler> _logger;

    public PaymentAuthorizationFailedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<PaymentAuthorizationFailedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(PaymentAuthorizationFailedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToPaymentAuthorizationFailedEvent();

        _outbox.AddOutboxMessage(
            _topics.Transactions,
            domainEvent.OrderId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added PaymentAuthorizationFailedEvent to outbox. PaymentId: {PaymentId}, OrderId: {OrderId}, Reason: {Reason}",
            domainEvent.PaymentId,
            domainEvent.OrderId,
            domainEvent.FailureInfo.Reason.Name);

        return Task.CompletedTask;
    }
}
