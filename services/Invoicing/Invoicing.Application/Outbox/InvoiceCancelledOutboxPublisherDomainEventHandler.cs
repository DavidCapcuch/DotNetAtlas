using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Domain.Invoices.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Fans out <see cref="InvoiceCancelledDomainEvent"/> to <c>invoicing.invoices</c>. Always
/// emitted alongside <c>CreditNoteIssuedDomainEvent</c> when the credit-note
/// command path runs — Notifications and BFF caches use the cancellation event to
/// invalidate the original invoice while the credit-note event delivers the reversal.
/// </summary>
public sealed class InvoiceCancelledOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceCancelledDomainEvent>
{
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly InvoicingTopicsOptions _topics;
    private readonly ILogger<InvoiceCancelledOutboxPublisherDomainEventHandler> _logger;

    public InvoiceCancelledOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        IOptions<InvoicingTopicsOptions> topics,
        ILogger<InvoiceCancelledOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(InvoiceCancelledDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToInvoiceCancelledEvent();

        _outbox.AddOutboxMessage(
            _topics.Invoices,
            domainEvent.BuyerId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Queued InvoiceCancelledEvent to outbox. InvoiceId: {InvoiceId}, CreditNoteId: {CreditNoteId}, CorrelationId: {CorrelationId}",
            domainEvent.InvoiceId,
            domainEvent.CreditNoteId,
            domainEvent.CorrelationId);

        return Task.CompletedTask;
    }
}
