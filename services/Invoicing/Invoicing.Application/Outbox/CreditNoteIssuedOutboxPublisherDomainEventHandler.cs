using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Domain.CreditNotes.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Fans out <see cref="CreditNoteIssuedDomainEvent"/> to <c>invoicing.invoices</c> via the
/// transactional outbox. Partition key is <c>BuyerId</c> so a buyer's fiscal stream
/// (invoices + their reversing credit notes) stays on a single partition for ordered
/// downstream consumption.
/// </summary>
public sealed class CreditNoteIssuedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<CreditNoteIssuedDomainEvent>
{
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<CreditNoteIssuedOutboxPublisherDomainEventHandler> _logger;

    public CreditNoteIssuedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<CreditNoteIssuedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(CreditNoteIssuedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToCreditNoteIssuedEvent();

        _outbox.AddOutboxMessage(
            _topics.Invoices,
            domainEvent.BuyerId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Queued CreditNoteIssuedEvent to outbox. CreditNoteId: {CreditNoteId}, CreditNoteNumber: {CreditNoteNumber}, OriginalInvoiceId: {OriginalInvoiceId}, CorrelationId: {CorrelationId}",
            domainEvent.CreditNoteId,
            domainEvent.CreditNoteNumber.Value,
            domainEvent.OriginalInvoiceId,
            domainEvent.CorrelationId);

        return Task.CompletedTask;
    }
}
