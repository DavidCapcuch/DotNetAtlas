using System.Text.Json;
using Invoicing.Application.Common.Data;
using Invoicing.Application.CreditNotes.Projections;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Inbound consumer for Ordering's <c>OrderCancelledEvent</c> on
/// <c>ordering.orders</c>. Upserts a <see cref="PendingCreditNote"/> row
/// keyed on <c>CorrelationId</c>, populating the order-cancel half. When
/// the refund half is already present, marks the row converged via
/// <see cref="PendingCreditNote.CompletedAtUtc"/>.
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderCancelledCreditNoteProjectionKafkaHandler> _logger;

    public OrderCancelledCreditNoteProjectionKafkaHandler(
        IInvoicingDbContext db,
        TimeProvider timeProvider,
        ILogger<OrderCancelledCreditNoteProjectionKafkaHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroOrderCancelledEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        var ct = context.ConsumerContext.WorkerStopped;
        var now = _timeProvider.GetUtcNow();
        var orderJson = SerializePayload(message);

        using var correlationScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["OrderId"] = message.OrderId,
            ["BuyerId"] = message.BuyerId,
            ["AtStatus"] = message.AtStatus.ToString(),
        });

        var (row, isNew) = await PendingProjectionUpsertHelper.GetOrAddAsync(
            _db.PendingCreditNotes,
            message.CorrelationId,
            () => new PendingCreditNote
            {
                CorrelationId = message.CorrelationId,
                OrderId = message.OrderId,
                BuyerId = message.BuyerId,
                OrderPayload = orderJson,
                FirstSeenAtUtc = now,
            },
            ct);

        if (!isNew)
        {
            if (row.OrderId is not null)
            {
                _logger.LogInformation(
                    "OrderCancelledEvent already projected for CorrelationId {CorrelationId}; no-op.",
                    message.CorrelationId);
                return;
            }

            row.OrderId = message.OrderId;
            row.BuyerId = message.BuyerId;
            row.OrderPayload = orderJson;

            if (row.PaymentId is not null && row.CompletedAtUtc is null)
            {
                row.CompletedAtUtc = now;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "OrderCancelledEvent projected (IsNew={IsNew}, Converged={Converged})",
            isNew,
            row.CompletedAtUtc is not null);
    }

    private static string SerializePayload(AvroOrderCancelledEvent message)
    {
        // See OrderConfirmedInvoiceProjectionKafkaHandler.SerializePayload for the rationale
        // on the hand-rolled DTO. The AtStatus enum is explicitly stringified for jsonb readability.
        return JsonSerializer.Serialize(new
        {
            message.OrderId,
            message.CorrelationId,
            message.BuyerId,
            message.Reason,
            AtStatus = message.AtStatus.ToString(),
            message.CancelledAtUtc,
        });
    }
}
