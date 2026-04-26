using System.Text.Json;
using Invoicing.Application.Common.Data;
using Invoicing.Application.CreditNotes.Projections;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using AvroPaymentRefundedEvent = Payments.Transactions.PaymentRefundedEvent;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Inbound consumer for Payments' <c>PaymentRefundedEvent</c> on
/// <c>payments.transactions</c>. Upserts a <see cref="PendingCreditNote"/>
/// row keyed on <c>CorrelationId</c>, populating the refund half. When the
/// order-cancel half is already present, marks the row converged via
/// <see cref="PendingCreditNote.CompletedAtUtc"/>.
/// </summary>
/// <remarks>
/// The credit note compensates the original captured payment, so the row's
/// <see cref="PendingCreditNote.PaymentId"/> stores
/// <c>PaymentRefundedEvent.PaymentTransactionId</c> (the original) — the
/// refund's own transaction id is preserved inside the JSON payload for M7.
/// </remarks>
internal sealed class PaymentRefundedCreditNoteProjectionKafkaHandler
    : IMessageHandler<AvroPaymentRefundedEvent>
{
    private readonly IInvoicingDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentRefundedCreditNoteProjectionKafkaHandler> _logger;

    public PaymentRefundedCreditNoteProjectionKafkaHandler(
        IInvoicingDbContext db,
        TimeProvider timeProvider,
        ILogger<PaymentRefundedCreditNoteProjectionKafkaHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroPaymentRefundedEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        var ct = context.ConsumerContext.WorkerStopped;
        var now = _timeProvider.GetUtcNow();
        var paymentJson = SerializePayload(message);

        using var correlationScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["PaymentTransactionId"] = message.PaymentTransactionId,
            ["RefundTransactionId"] = message.RefundTransactionId,
        });

        var (row, isNew) = await PendingProjectionUpsertHelper.GetOrAddAsync(
            _db.PendingCreditNotes,
            message.CorrelationId,
            () => new PendingCreditNote
            {
                CorrelationId = message.CorrelationId,
                PaymentId = message.PaymentTransactionId,
                PaymentPayload = paymentJson,
                FirstSeenAtUtc = now,
            },
            ct);

        if (!isNew)
        {
            if (row.PaymentId is not null)
            {
                _logger.LogInformation(
                    "PaymentRefundedEvent already projected for CorrelationId {CorrelationId}; no-op.",
                    message.CorrelationId);
                return;
            }

            row.PaymentId = message.PaymentTransactionId;
            row.PaymentPayload = paymentJson;

            if (row.OrderId is not null && row.CompletedAtUtc is null)
            {
                row.CompletedAtUtc = now;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PaymentRefundedEvent projected (IsNew={IsNew}, Converged={Converged})",
            isNew,
            row.CompletedAtUtc is not null);
    }

    private static string SerializePayload(AvroPaymentRefundedEvent message)
    {
        return JsonSerializer.Serialize(new
        {
            message.CorrelationId,
            message.UserId,
            message.PaymentTransactionId,
            message.RefundTransactionId,
            RefundedAmount = (decimal)message.RefundedAmount,
            message.Currency,
            message.RefundedAtUtc,
        });
    }
}
