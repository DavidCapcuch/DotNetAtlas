using System.Text.Json;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Application.Invoices.Projections;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using AvroPaymentCapturedEvent = Payments.Transactions.PaymentCapturedEvent;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Inbound consumer for Payments' <c>PaymentCapturedEvent</c> on
/// <c>payments.transactions</c>. Upserts a <see cref="PendingInvoice"/> row
/// keyed on <c>OrderId</c>, populating the payment half. When the
/// order half is already present, marks the row converged AND dispatches
/// <see cref="IssueInvoiceCommand"/> in the same inbox transaction.
/// </summary>
/// <remarks>
/// Mirror of <see cref="OrderConfirmedInvoiceProjectionKafkaHandler"/> for
/// the other half of the convergence pair. Same idempotency guarantees
/// (inbox dedup on <c>MessageId</c>; payment-half no-op on same-OrderId
/// re-arrival; the command is idempotent on <c>IssuedInvoiceId</c>).
/// </remarks>
internal sealed class PaymentCapturedInvoiceProjectionKafkaHandler
    : IMessageHandler<AvroPaymentCapturedEvent>
{
    private readonly IInvoicingDbContext _db;
    private readonly ICommandHandler<IssueInvoiceCommand, Guid> _issueInvoiceHandler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentCapturedInvoiceProjectionKafkaHandler> _logger;

    public PaymentCapturedInvoiceProjectionKafkaHandler(
        IInvoicingDbContext db,
        ICommandHandler<IssueInvoiceCommand, Guid> issueInvoiceHandler,
        TimeProvider timeProvider,
        ILogger<PaymentCapturedInvoiceProjectionKafkaHandler> logger)
    {
        _db = db;
        _issueInvoiceHandler = issueInvoiceHandler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroPaymentCapturedEvent message)
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
        });

        var (row, isNew) = await PendingProjectionUpsertHelper.GetOrAddAsync(
            _db.PendingInvoices,
            orderId,
            () => new PendingInvoice
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
                    "PaymentCapturedEvent already projected for OrderId {OrderId}; no-op.",
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
            "PaymentCapturedEvent projected (IsNew={IsNew}, Converged={Converged})",
            isNew,
            row.CompletedAtUtc is not null);

        if (convergedNow)
        {
            // M7 — see OrderConfirmedInvoiceProjectionKafkaHandler for the convergence
            // dispatch rationale (inbox-transaction join + idempotent M7 handler).
            var result = await _issueInvoiceHandler.HandleAsync(
                new IssueInvoiceCommand { OrderId = orderId },
                ct);
            if (result.IsFailed)
            {
                throw new InvalidOperationException(
                    $"IssueInvoiceCommand failed after convergence on OrderId {orderId}: "
                        + string.Join("; ", result.Errors.Select(e => e.Message)));
            }
        }
    }

    private static string SerializePayload(AvroPaymentCapturedEvent message)
    {
        // See OrderConfirmedInvoiceProjectionKafkaHandler.SerializePayload for the rationale
        // on the hand-rolled DTO. Avro.AvroDecimal needs explicit conversion — System.Text.Json
        // doesn't know about it; cast to decimal first (AvroDecimal exposes an explicit cast).
        return JsonSerializer.Serialize(new
        {
            message.UserId,
            message.PaymentTransactionId,
            message.AuthorizationId,
            Amount = (decimal)message.Amount,
            message.Currency,
            message.CapturedAtUtc,
        });
    }
}
