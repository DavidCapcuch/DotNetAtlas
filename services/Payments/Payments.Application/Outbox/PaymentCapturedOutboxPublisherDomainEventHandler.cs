using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Domain.Transactions.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Outbox;

/// <summary>
/// Fan-out from <see cref="PaymentCapturedDomainEvent"/> to the external Avro event on
/// <c>payments.transactions</c>. Consumed by Invoicing (invoice issuance) and
/// PaymentProcessingSaga (capture-success confirmation). Note: the aggregate also raises
/// <see cref="PaymentCompletedDomainEvent"/> immediately after — but that internal event has
/// no Payments-side outbox publisher; <c>PaymentCompletedEvent</c> is produced by
/// PaymentProcessingSaga per events-catalog.md.
/// </summary>
public sealed class PaymentCapturedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<PaymentCapturedDomainEvent>
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<PaymentCapturedOutboxPublisherDomainEventHandler> _logger;

    public PaymentCapturedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<PaymentCapturedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(PaymentCapturedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToPaymentCapturedEvent();

        _outbox.AddOutboxMessage(
            _topics.Transactions,
            domainEvent.OrderId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added PaymentCapturedEvent to outbox. PaymentId: {PaymentId}, OrderId: {OrderId}",
            domainEvent.PaymentId,
            domainEvent.OrderId);

        return Task.CompletedTask;
    }
}
