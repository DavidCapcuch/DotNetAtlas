using System.Globalization;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Application.Common.Notifications;
using Invoicing.Domain.Invoices.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Fans out <see cref="InvoiceDeliveryRequestedDomainEvent"/> as a generic
/// <c>SendEmailNotificationCommand</c> on the Notifications email-commands topic.
/// Runs inside the same EF transaction as the aggregate save (the
/// <c>DispatchDomainEventsInterceptor</c> dispatches before <c>SaveChangesAsync</c>
/// commits), so the outbox row is atomic with the aggregate.
/// No blob-store access — the email body links to the buyer portal, which mints a SAS
/// server-side via the existing GET endpoint (issue #131).
/// </summary>
public sealed class InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceDeliveryRequestedDomainEvent>
{
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly IInvoicingDbContext _db;
    private readonly InvoicingTopicsOptions _topics;
    private readonly BuyerPortalOptions _portal;
    private readonly TimeProvider _clock;
    private readonly ILogger<InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler> _logger;

    public InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        IInvoicingDbContext db,
        IOptions<InvoicingTopicsOptions> topics,
        IOptions<BuyerPortalOptions> portal,
        TimeProvider clock,
        ILogger<InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _db = db;
        _topics = topics.Value;
        _portal = portal.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(InvoiceDeliveryRequestedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var invoice = await _db.Invoices
            .SingleOrDefaultAsync(i => i.Id == domainEvent.InvoiceId, ct)
            ?? throw new DataIntegrityException(
                "Invoicing.InvoiceMissingOnDeliveryRequest",
                $"No invoice for id '{domainEvent.InvoiceId}' (raised by domain event in same transaction).");

        var portalUrl = $"{_portal.BaseUrl.TrimEnd('/')}/invoices/{domainEvent.InvoiceId}";

        var command = new SendEmailNotificationCommand
        {
            UserId = domainEvent.BuyerId,
            TemplateId = "invoicing.invoice-delivered",
            TemplateData = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = invoice.InvoiceNumber!.Value,
                ["TotalAmount"] = invoice.Total.Amount.ToString(CultureInfo.InvariantCulture),
                ["Currency"] = invoice.Total.Currency.Name,
                ["ViewInvoiceUrl"] = portalUrl,
            },
            IdempotencyKey = $"invoice-delivered-{domainEvent.InvoiceId}-{domainEvent.Attempt}",
            OccurredOnUtc = _clock.GetUtcNow().UtcDateTime,
        };

        _outbox.AddOutboxMessage(_topics.NotificationsEmailCommands, domainEvent.BuyerId.ToString(), command);

        _logger.LogInformation(
            "Queued invoice-delivery email request. InvoiceId={InvoiceId}, Attempt={Attempt}",
            domainEvent.InvoiceId,
            domainEvent.Attempt);
    }
}
