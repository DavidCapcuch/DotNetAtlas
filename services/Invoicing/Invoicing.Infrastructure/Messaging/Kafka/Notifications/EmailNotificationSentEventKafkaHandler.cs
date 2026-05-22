using Invoicing.Application.Common.Data;
using Invoicing.Domain.Invoices;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Infrastructure.Messaging.Kafka.Notifications;

/// <summary>
/// Invoicing-side consumer for the generic <see cref="EmailNotificationSentEvent"/>. Filters by
/// <c>TemplateId</c> prefix <c>"invoicing."</c> and only handles
/// <c>"invoicing.invoice-delivered"</c>. Parses <c>InvoiceId</c> from the
/// <c>IdempotencyKey</c> (<c>"invoice-delivered-{guid}-{attempt}"</c>), loads the
/// <see cref="Invoice"/>, and calls <see cref="Invoice.Deliver"/>, which raises
/// <see cref="Invoicing.Domain.Invoices.Events.InvoiceDeliveredDomainEvent"/> →
/// <c>InvoiceDeliveredOutboxPublisherDomainEventHandler</c> → Avro outbox row.
/// </summary>
public sealed class EmailNotificationSentEventKafkaHandler : IMessageHandler<EmailNotificationSentEvent>
{
    private const string InvoicingPrefix = "invoicing.";
    private const string InvoiceDeliveredTemplate = "invoicing.invoice-delivered";

    private readonly IInvoicingDbContext _db;
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailNotificationSentEventKafkaHandler> _logger;

    public EmailNotificationSentEventKafkaHandler(
        IInvoicingDbContext db,
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        TimeProvider clock,
        ILogger<EmailNotificationSentEventKafkaHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, EmailNotificationSentEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        if (!message.TemplateId.StartsWith(InvoicingPrefix, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(message.TemplateId, InvoiceDeliveredTemplate, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Unknown invoicing-prefixed template '{TemplateId}'; ignoring.",
                message.TemplateId);
            return;
        }

        if (!TryParseInvoiceIdFromIdempotencyKey(message.IdempotencyKey, out var invoiceId))
        {
            throw new DataIntegrityException(
                "Invoicing.MalformedDeliveryIdempotencyKey",
                $"Cannot parse InvoiceId from IdempotencyKey '{message.IdempotencyKey}'.");
        }

        var token = context.ConsumerContext.WorkerStopped;

        await _outbox.Database.EnsureTransactionAsync(async () =>
        {
            var invoice = await _db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, token)
                ?? throw new DataIntegrityException(
                    "Invoicing.InvoiceUnknownOnDeliveryConfirmation",
                    $"No invoice for id '{invoiceId}'.");

            var deliverResult = invoice.Deliver(_clock.GetUtcNow());
            if (deliverResult.IsFailed)
            {
                _logger.LogWarning(
                    "Invoice.Deliver no-op for {InvoiceId}: {Errors}",
                    invoiceId,
                    string.Join("; ", deliverResult.Errors.Select(e => e.Message)));
                return;
            }

            await _db.SaveChangesAsync(token);
        }, token);
    }

    private static bool TryParseInvoiceIdFromIdempotencyKey(string key, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        // Key format: "invoice-delivered-{guid}-{attempt}"
        // The guid itself has 4 hyphens, so strip the known prefix and then strip the
        // trailing "-{attempt}" part by cutting at the last dash.
        const string prefix = "invoice-delivered-";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = key.AsSpan(prefix.Length);
        var lastDash = rest.LastIndexOf('-');
        if (lastDash < 0)
        {
            return false;
        }

        return Guid.TryParse(rest[..lastDash], out id);
    }
}
