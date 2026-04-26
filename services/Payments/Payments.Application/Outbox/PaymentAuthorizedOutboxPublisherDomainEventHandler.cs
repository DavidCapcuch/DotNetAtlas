using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Domain.Transactions.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Outbox;

/// <summary>
/// In-process fan-out from <see cref="PaymentAuthorizedDomainEvent"/> to the external Avro
/// event on the <c>payments.transactions</c> topic. The command handler owns the transaction
/// boundary; this handler only enqueues the outbox row.
/// </summary>
public sealed class PaymentAuthorizedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<PaymentAuthorizedDomainEvent>
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly PaymentsTopicsOptions _topics;
    private readonly ILogger<PaymentAuthorizedOutboxPublisherDomainEventHandler> _logger;

    public PaymentAuthorizedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IOptions<PaymentsTopicsOptions> topics,
        ILogger<PaymentAuthorizedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(PaymentAuthorizedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToPaymentAuthorizedEvent();

        _outbox.AddOutboxMessage(
            _topics.Transactions,
            domainEvent.CorrelationId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added PaymentAuthorizedEvent to outbox. PaymentId: {PaymentId}, CorrelationId: {CorrelationId}",
            domainEvent.PaymentId,
            domainEvent.CorrelationId);

        return Task.CompletedTask;
    }
}
