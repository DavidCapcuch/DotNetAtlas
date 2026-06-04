using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Domain.Invoices.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Fans out <see cref="InvoiceIssuedDomainEvent"/> to <c>invoicing.invoices</c> via the
/// transactional outbox. Runs inside the same EF transaction as the aggregate save (the
/// <c>DispatchDomainEventsInterceptor</c> dispatches before <c>SaveChangesAsync</c>
/// commits), so the outbox row and the <c>Invoice</c> aggregate row are atomic — no
/// half-issued invoice is ever observable. Partition key is <c>BuyerId</c> per
/// <c>docs/bc-design/invoicing.md § 6</c> so a buyer's fiscal stream stays on one partition.
/// </summary>
public sealed class InvoiceIssuedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceIssuedDomainEvent>
{
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<InvoiceIssuedOutboxPublisherDomainEventHandler> _logger;

    public InvoiceIssuedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<InvoiceIssuedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(InvoiceIssuedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var integrationEvent = domainEvent.ToInvoiceIssuedEvent();

        _outbox.AddOutboxMessage(
            _topics.Invoices,
            domainEvent.BuyerId.ToString(),
            integrationEvent);

        _logger.LogInformation(
            "Queued InvoiceIssuedEvent to outbox. InvoiceId: {InvoiceId}, InvoiceNumber: {InvoiceNumber}, OrderId: {OrderId}",
            domainEvent.InvoiceId,
            domainEvent.InvoiceNumber.Value,
            domainEvent.OrderId);

        return Task.CompletedTask;
    }
}
