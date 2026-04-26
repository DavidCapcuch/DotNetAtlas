using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Domain.Transactions.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Outbox;

/// <summary>
/// Fan-out from <see cref="PaymentVoidedDomainEvent"/> to the external Avro event on
/// <c>payments.transactions</c>. Consumed by PaymentProcessingSaga as confirmation of the
/// pre-capture compensation path.
/// </summary>
public sealed class PaymentVoidedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<PaymentVoidedDomainEvent>
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly PaymentsTopicsOptions _topics;
    private readonly ILogger<PaymentVoidedOutboxPublisherDomainEventHandler> _logger;

    public PaymentVoidedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IOptions<PaymentsTopicsOptions> topics,
        ILogger<PaymentVoidedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(PaymentVoidedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToPaymentVoidedEvent();

        _outbox.AddOutboxMessage(
            _topics.Transactions,
            domainEvent.CorrelationId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added PaymentVoidedEvent to outbox. PaymentId: {PaymentId}, CorrelationId: {CorrelationId}",
            domainEvent.PaymentId,
            domainEvent.CorrelationId);

        return Task.CompletedTask;
    }
}
