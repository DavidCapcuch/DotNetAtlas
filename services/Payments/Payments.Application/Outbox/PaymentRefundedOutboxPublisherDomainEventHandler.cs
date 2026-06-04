using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Domain.Transactions.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Outbox;

/// <summary>
/// Fan-out from <see cref="PaymentRefundedDomainEvent"/> to the external Avro event on
/// <c>payments.transactions</c>. Multi-consumer: Checkout saga (cancel-post-capture
/// confirmation), Notifications, Invoicing (credit-note trigger).
/// </summary>
public sealed class PaymentRefundedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<PaymentRefundedDomainEvent>
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<PaymentRefundedOutboxPublisherDomainEventHandler> _logger;

    public PaymentRefundedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<PaymentRefundedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(PaymentRefundedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToPaymentRefundedEvent();

        _outbox.AddOutboxMessage(
            _topics.Transactions,
            domainEvent.OrderId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added PaymentRefundedEvent to outbox. PaymentId: {PaymentId}, OrderId: {OrderId}",
            domainEvent.PaymentId,
            domainEvent.OrderId);

        return Task.CompletedTask;
    }
}
