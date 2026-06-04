using System.Globalization;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Application.Common.Notifications;
using Invoicing.Domain.Invoices.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Fans out <see cref="InvoiceDeliveryRequestedDomainEvent"/> as a channel-agnostic
/// <c>NotifyUserCommand</c> on the Notifications notify-commands topic (ADR-0031). Runs inside the
/// same EF transaction as the aggregate save (the <c>DispatchDomainEventsInterceptor</c> dispatches
/// before <c>SaveChangesAsync</c> commits), so the outbox row is atomic with the aggregate — and
/// with the <c>delivery_notification_id</c> the aggregate persisted. Template data (invoice number,
/// total) is read directly from the domain event — the interceptor fires before the invoice row
/// reaches the DB, so a re-query would fail to find it (see D2 wire-up notes). No blob-store access —
/// the email body links to the buyer portal, which mints a SAS server-side via the existing GET
/// endpoint (issue #131).
/// </summary>
public sealed class InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceDeliveryRequestedDomainEvent>
{
    private const string InvoiceDeliveredTemplateKey = "invoicing.invoice-delivered";

    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly BuyerPortalOptions _portal;
    private readonly TimeProvider _clock;
    private readonly ILogger<InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler> _logger;

    public InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        IOptions<BuyerPortalOptions> portal,
        TimeProvider clock,
        ILogger<InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _portal = portal.Value;
        _clock = clock;
        _logger = logger;
    }

    public Task Handle(InvoiceDeliveryRequestedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var portalUrl = $"{_portal.BaseUrl.TrimEnd('/')}/invoices/{domainEvent.InvoiceId}";

        var command = new NotifyUserCommand
        {
            NotificationId = domainEvent.NotificationId,
            RecipientUserId = domainEvent.BuyerId,
            TemplateKey = InvoiceDeliveredTemplateKey,
            Payload = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = domainEvent.InvoiceNumber.Value,
                ["TotalAmount"] = domainEvent.Total.Amount.ToString(CultureInfo.InvariantCulture),
                ["Currency"] = domainEvent.Total.Currency.Name,
                ["ViewInvoiceUrl"] = portalUrl,
            },
            OccurredOnUtc = _clock.GetUtcNow().UtcDateTime,
        };

        // Key on RecipientUserId (= BuyerId), matching the Notifications command topic partition key.
        _outbox.AddOutboxMessage(_topics.NotificationsNotifyCommands, domainEvent.BuyerId.ToString(), command);

        _logger.LogInformation(
            "Queued invoice-delivery notification. InvoiceId={InvoiceId}, NotificationId={NotificationId}",
            domainEvent.InvoiceId,
            domainEvent.NotificationId);

        return Task.CompletedTask;
    }
}
