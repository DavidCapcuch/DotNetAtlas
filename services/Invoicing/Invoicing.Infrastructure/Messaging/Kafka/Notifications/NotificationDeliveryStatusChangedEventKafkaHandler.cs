using Invoicing.Application.Common.Data;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Infrastructure.Messaging.Kafka.Notifications;

/// <summary>
/// Invoicing-side consumer for the generalized <see cref="NotificationDeliveryStatusChangedEvent"/>
/// (ADR-0031). Acts only on a successful email delivery of an invoicing notification
/// (<c>Channel == Email</c>, <c>Status == Dispatched</c>, <c>TemplateKey</c> prefix
/// <c>"invoicing."</c>), correlates the <see cref="Invoicing.Domain.Invoices.Invoice"/> by the stored
/// <c>delivery_notification_id</c> (= <c>NotificationId</c> — a typed field read, replacing the v1
/// <c>invoice-delivered-{guid}-{attempt}</c> string parse), and drives <c>Issued → Delivered</c>,
/// which raises <see cref="Invoicing.Domain.Invoices.Events.InvoiceDeliveredDomainEvent"/> →
/// <c>InvoiceDeliveredOutboxPublisherDomainEventHandler</c> → Avro outbox row.
/// </summary>
public sealed class NotificationDeliveryStatusChangedEventKafkaHandler
    : IMessageHandler<NotificationDeliveryStatusChangedEvent>
{
    private const string InvoicingPrefix = "invoicing.";
    private const string EmailChannel = "Email";

    private readonly IInvoicingDbContext _db;
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationDeliveryStatusChangedEventKafkaHandler> _logger;

    public NotificationDeliveryStatusChangedEventKafkaHandler(
        IInvoicingDbContext db,
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        TimeProvider clock,
        ILogger<NotificationDeliveryStatusChangedEventKafkaHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, NotificationDeliveryStatusChangedEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        if (!string.Equals(message.Channel, EmailChannel, StringComparison.OrdinalIgnoreCase)
            || message.Status != NotificationDeliveryStatus.Dispatched
            || !message.TemplateKey.StartsWith(InvoicingPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var token = context.ConsumerContext.WorkerStopped;

        await _outbox.Database.EnsureTransactionAsync(async () =>
        {
            var invoice = await _db.Invoices
                .SingleOrDefaultAsync(i => i.DeliveryNotificationId == message.NotificationId, token)
                ?? throw new DataIntegrityException(
                    "Invoicing.InvoiceUnknownOnDeliveryConfirmation",
                    $"No invoice for NotificationId '{message.NotificationId}'.");

            var deliverResult = invoice.Deliver(_clock.GetUtcNow());
            if (deliverResult.IsFailed)
            {
                _logger.LogWarning(
                    "Invoice.Deliver no-op for {InvoiceId} (NotificationId={NotificationId}): {Errors}",
                    invoice.Id,
                    message.NotificationId,
                    string.Join("; ", deliverResult.Errors.Select(e => e.Message)));
                return;
            }

            await _db.SaveChangesAsync(token);
        }, token);
    }
}
