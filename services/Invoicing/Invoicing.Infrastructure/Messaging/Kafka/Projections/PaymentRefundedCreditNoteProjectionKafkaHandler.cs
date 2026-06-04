using System.Text.Json;
using Invoicing.Application.Common.Data;
using Invoicing.Application.CreditNotes.IssueCreditNote;
using Invoicing.Application.CreditNotes.Projections;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using AvroPaymentRefundedEvent = Payments.Transactions.PaymentRefundedEvent;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Inbound consumer for Payments' <c>PaymentRefundedEvent</c> on
/// <c>payments.transactions</c>. Upserts a <see cref="PendingCreditNote"/>
/// row keyed on <c>OrderId</c>, populating the refund half. When the
/// order-cancel half is already present, marks the row converged AND dispatches
/// <see cref="IssueCreditNoteCommand"/> in the same inbox transaction.
/// </summary>
/// <remarks>
/// The credit note compensates the original captured payment, so the row's
/// <see cref="PendingCreditNote.PaymentId"/> stores
/// <c>PaymentRefundedEvent.PaymentTransactionId</c> (the original) — the
/// refund's own transaction id is preserved inside the JSON payload.
/// </remarks>
internal sealed class PaymentRefundedCreditNoteProjectionKafkaHandler
    : IMessageHandler<AvroPaymentRefundedEvent>
{
    private readonly IInvoicingDbContext _db;
    private readonly ICommandHandler<IssueCreditNoteCommand, Guid> _issueCreditNoteHandler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentRefundedCreditNoteProjectionKafkaHandler> _logger;

    public PaymentRefundedCreditNoteProjectionKafkaHandler(
        IInvoicingDbContext db,
        ICommandHandler<IssueCreditNoteCommand, Guid> issueCreditNoteHandler,
        TimeProvider timeProvider,
        ILogger<PaymentRefundedCreditNoteProjectionKafkaHandler> logger)
    {
        _db = db;
        _issueCreditNoteHandler = issueCreditNoteHandler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroPaymentRefundedEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        // ADR-0029 — OrderId is the cross-BC convergence key; both halves carry it.
        var orderId = message.OrderId;
        var ct = context.ConsumerContext.WorkerStopped;
        var now = _timeProvider.GetUtcNow();
        var paymentJson = SerializePayload(message);

        // Cross-process correlation is the W3C traceId (OpenTelemetry); push OrderId so per-order
        // log queries work in Seq.
        using var localScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OrderId"] = orderId,
            ["PaymentTransactionId"] = message.PaymentTransactionId,
            ["RefundTransactionId"] = message.RefundTransactionId,
        });

        var (row, isNew) = await PendingProjectionUpsertHelper.GetOrAddAsync(
            _db.PendingCreditNotes,
            orderId,
            () => new PendingCreditNote
            {
                OrderId = orderId,
                PaymentId = message.PaymentTransactionId,
                PaymentPayload = paymentJson,
                FirstSeenAtUtc = now,
            },
            ct);

        var convergedNow = false;
        if (!isNew)
        {
            if (row.PaymentId is not null)
            {
                _logger.LogInformation(
                    "PaymentRefundedEvent already projected for OrderId {OrderId}; no-op.",
                    orderId);
                return;
            }

            row.PaymentId = message.PaymentTransactionId;
            row.PaymentPayload = paymentJson;

            if (row.OrderPayload is not null && row.CompletedAtUtc is null)
            {
                row.CompletedAtUtc = now;
                convergedNow = true;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PaymentRefundedEvent projected (IsNew={IsNew}, Converged={Converged})",
            isNew,
            row.CompletedAtUtc is not null);

        if (convergedNow)
        {
            // M7 — see OrderCancelledCreditNoteProjectionKafkaHandler for the convergence
            // dispatch rationale. Same Result.Fail-vs-throw split.
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

    private static string SerializePayload(AvroPaymentRefundedEvent message)
    {
        return JsonSerializer.Serialize(new
        {
            message.UserId,
            message.PaymentTransactionId,
            message.RefundTransactionId,
            RefundedAmount = (decimal)message.RefundedAmount,
            message.Currency,
            message.RefundedAtUtc,
        });
    }
}
