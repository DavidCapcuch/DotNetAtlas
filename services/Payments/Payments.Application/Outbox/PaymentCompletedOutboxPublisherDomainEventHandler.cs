using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Domain.Transactions.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Outbox;

/// <summary>
/// Fan-out from <see cref="PaymentCompletedDomainEvent"/> to the external Avro
/// <c>PaymentCompletedEvent</c> on <c>payments.transactions</c>. Per ADR-0026 the Payments
/// service owns all its lifecycle integration events including the terminals — this publisher is
/// symmetric with the Authorized / Captured / Voided / Refunded handlers and replaces the former
/// PaymentProcessingSaga-side publication. Co-raised with <see cref="PaymentCapturedDomainEvent"/>
/// on a successful capture; the Checkout saga consumes the resulting <c>PaymentCompletedEvent</c>
/// to finalize the order.
/// </summary>
public sealed class PaymentCompletedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<PaymentCompletedDomainEvent>
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<PaymentCompletedOutboxPublisherDomainEventHandler> _logger;

    public PaymentCompletedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<PaymentCompletedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(PaymentCompletedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToPaymentCompletedEvent();

        _outbox.AddOutboxMessage(
            _topics.Transactions,
            domainEvent.OrderId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added PaymentCompletedEvent to outbox. PaymentId: {PaymentId}, OrderId: {OrderId}",
            domainEvent.PaymentId,
            domainEvent.OrderId);

        return Task.CompletedTask;
    }
}
