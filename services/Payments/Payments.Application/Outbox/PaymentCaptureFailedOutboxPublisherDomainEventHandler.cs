using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Domain.Transactions.Events;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Application.Outbox;

/// <summary>
/// Fan-out from <see cref="PaymentCaptureFailedDomainEvent"/> to the external Avro event on
/// <c>payments.transactions</c>. Co-emitted with <see cref="PaymentFailedDomainEvent"/>, which
/// has no Payments-side outbox publisher (it is produced by PaymentProcessingSaga).
/// </summary>
public sealed class PaymentCaptureFailedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<PaymentCaptureFailedDomainEvent>
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<PaymentCaptureFailedOutboxPublisherDomainEventHandler> _logger;

    public PaymentCaptureFailedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<PaymentCaptureFailedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(PaymentCaptureFailedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToPaymentCaptureFailedEvent();

        _outbox.AddOutboxMessage(
            _topics.Transactions,
            domainEvent.OrderId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Added PaymentCaptureFailedEvent to outbox. PaymentId: {PaymentId}, OrderId: {OrderId}, Reason: {Reason}",
            domainEvent.PaymentId,
            domainEvent.OrderId,
            domainEvent.FailureInfo.Reason.Name);

        return Task.CompletedTask;
    }
}
