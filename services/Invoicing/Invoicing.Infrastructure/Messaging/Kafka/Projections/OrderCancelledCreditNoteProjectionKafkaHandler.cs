using System.Text.Json;
using Invoicing.Application.Common.Data;
using Invoicing.Application.CreditNotes.IssueCreditNote;
using Invoicing.Application.CreditNotes.Projections;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Inbound consumer for Ordering's <c>OrderCancelledEvent</c> on
/// <c>ordering.orders</c>. Upserts a <see cref="PendingCreditNote"/> row
/// keyed on <c>OrderId</c>, populating the order-cancel half. When
/// the refund half is already present, marks the row converged AND dispatches
/// <see cref="IssueCreditNoteCommand"/> in the same inbox transaction.
/// </summary>
/// <remarks>
/// Same convergence + idempotency contract as
/// <see cref="OrderConfirmedInvoiceProjectionKafkaHandler"/>; targets the
/// credit-note projection table instead of <c>pending_invoices</c>.
/// </remarks>
internal sealed class OrderCancelledCreditNoteProjectionKafkaHandler
    : IMessageHandler<AvroOrderCancelledEvent>
{
    private readonly IInvoicingDbContext _db;
    private readonly ICommandHandler<IssueCreditNoteCommand, Guid> _issueCreditNoteHandler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderCancelledCreditNoteProjectionKafkaHandler> _logger;

    public OrderCancelledCreditNoteProjectionKafkaHandler(
        IInvoicingDbContext db,
        ICommandHandler<IssueCreditNoteCommand, Guid> issueCreditNoteHandler,
        TimeProvider timeProvider,
        ILogger<OrderCancelledCreditNoteProjectionKafkaHandler> logger)
    {
        _db = db;
        _issueCreditNoteHandler = issueCreditNoteHandler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroOrderCancelledEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        // ADR-0029/0030 — OrderId is the cross-BC convergence key; both halves carry it.
        var orderId = message.OrderId;
        var ct = context.ConsumerContext.WorkerStopped;
        var now = _timeProvider.GetUtcNow();
        var orderJson = SerializePayload(message);

        // ADR-0008 — log/trace correlation flows from the Kafka header via
        // Serilog LogContext. Do not push "CorrelationId" into this BeginScope;
        // the inner-most scope would shadow the header-authoritative value.
        using var localScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OrderId"] = orderId,
            ["BuyerId"] = message.BuyerId,
            ["AtStatus"] = message.AtStatus.ToString(),
        });

        var (row, isNew) = await PendingProjectionUpsertHelper.GetOrAddAsync(
            _db.PendingCreditNotes,
            orderId,
            () => new PendingCreditNote
            {
                OrderId = orderId,
                BuyerId = message.BuyerId,
                OrderPayload = orderJson,
                FirstSeenAtUtc = now,
            },
            ct);

        var convergedNow = false;
        if (!isNew)
        {
            if (row.OrderPayload is not null)
            {
                _logger.LogInformation(
                    "OrderCancelledEvent already projected for OrderId {OrderId}; no-op.",
                    orderId);
                return;
            }

            row.BuyerId = message.BuyerId;
            row.OrderPayload = orderJson;

            if (row.PaymentId is not null && row.CompletedAtUtc is null)
            {
                row.CompletedAtUtc = now;
                convergedNow = true;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "OrderCancelledEvent projected (IsNew={IsNew}, Converged={Converged})",
            isNew,
            row.CompletedAtUtc is not null);

        if (convergedNow)
        {
            // M7 — dispatch the credit-note issuance command inside the inbox transaction.
            // Validation-style failures (e.g., already-cancelled invoice) come back as
            // Result.Fail and are logged; the inbox row still commits so we don't loop.
            // Bug-class failures throw and roll back the whole transaction.
            var result = await _issueCreditNoteHandler.HandleAsync(
                new IssueCreditNoteCommand { OrderId = orderId },
                ct);
            if (result.IsFailed)
            {
                _logger.LogWarning(
                    "IssueCreditNoteCommand returned Result.Fail after convergence on OrderId {OrderId}: {Errors}",
                    orderId,
                    string.Join("; ", result.Errors.Select(e => e.Message)));
            }
        }
    }

    private static string SerializePayload(AvroOrderCancelledEvent message)
    {
        // See OrderConfirmedInvoiceProjectionKafkaHandler.SerializePayload for the rationale
        // on the hand-rolled DTO. The AtStatus enum is explicitly stringified for jsonb readability.
        //
        // Per ADR-0020 (Wave 1.6) the Avro event is a Summary Event — Items, TotalAmount,
        // Currency and BillingAddress travel with it. Persisting them into OrderPayload
        // jsonb means M8's IssueCreditNoteCommandHandler can construct the credit note
        // from the converged pending_credit_notes row without an HTTP round-trip.
        return JsonSerializer.Serialize(new
        {
            message.OrderId,
            message.BuyerId,
            message.Reason,
            AtStatus = message.AtStatus.ToString(),
            message.CancelledAtUtc,
            Items = message.Items?.Select(i => new
            {
                i.ProductId,
                i.Sku,
                i.Name,
                i.Quantity,
                UnitPriceAmount = (decimal)i.UnitPriceAmount,
                LineTotalAmount = (decimal)i.LineTotalAmount,
            }).ToList(),
            TotalAmount = message.TotalAmount.HasValue ? (decimal?)message.TotalAmount.Value : null,
            message.Currency,
            BillingAddress = message.BillingAddress is null ? null : new
            {
                message.BillingAddress.Street1,
                message.BillingAddress.Street2,
                message.BillingAddress.City,
                message.BillingAddress.State,
                message.BillingAddress.PostalCode,
                message.BillingAddress.CountryCode,
            },
        });
    }
}
