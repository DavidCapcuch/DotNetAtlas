using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Domain.Invoices.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Fans out <see cref="InvoiceDeliveredDomainEvent"/> to <c>invoicing.invoices</c> via the
/// transactional outbox. Runs inside the same EF transaction as the aggregate save (the
/// <c>DispatchDomainEventsInterceptor</c> dispatches before <c>SaveChangesAsync</c>
/// commits), so the outbox row and the <c>Invoice</c> aggregate row are atomic.
/// Partition key is <c>BuyerId</c> so a buyer's fiscal stream stays on one partition.
/// </summary>
public sealed class InvoiceDeliveredOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceDeliveredDomainEvent>
{
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly InvoicingTopicsOptions _topics;
    private readonly ILogger<InvoiceDeliveredOutboxPublisherDomainEventHandler> _logger;

    public InvoiceDeliveredOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        IOptions<InvoicingTopicsOptions> topics,
        ILogger<InvoiceDeliveredOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(InvoiceDeliveredDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToInvoiceDeliveredEvent();

        _outbox.AddOutboxMessage(
            _topics.Invoices,
            domainEvent.BuyerId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Queued InvoiceDeliveredEvent to outbox. InvoiceId: {InvoiceId}, Channel: {Channel}, CorrelationId: {CorrelationId}",
            domainEvent.InvoiceId,
            domainEvent.Channel.Name,
            domainEvent.CorrelationId);

        return Task.CompletedTask;
    }
}
