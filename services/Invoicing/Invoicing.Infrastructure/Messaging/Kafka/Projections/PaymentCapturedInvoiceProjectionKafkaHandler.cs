using System.Text.Json;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.Projections;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using AvroPaymentCapturedEvent = Payments.Transactions.PaymentCapturedEvent;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Inbound consumer for Payments' <c>PaymentCapturedEvent</c> on
/// <c>payments.transactions</c>. Upserts a <see cref="PendingInvoice"/> row
/// keyed on <c>CorrelationId</c>, populating the payment half. When the
/// order half is already present, marks the row converged via
/// <see cref="PendingInvoice.CompletedAtUtc"/>.
/// </summary>
/// <remarks>
/// Mirror of <see cref="OrderConfirmedInvoiceProjectionKafkaHandler"/> for
/// the other half of the convergence pair. Same idempotency guarantees
/// (inbox dedup on <c>MessageId</c>; payment-half no-op on same-CorrelationId
/// re-arrival).
/// </remarks>
internal sealed class PaymentCapturedInvoiceProjectionKafkaHandler
    : IMessageHandler<AvroPaymentCapturedEvent>
{
    private readonly IInvoicingDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentCapturedInvoiceProjectionKafkaHandler> _logger;

    public PaymentCapturedInvoiceProjectionKafkaHandler(
        IInvoicingDbContext db,
        TimeProvider timeProvider,
        ILogger<PaymentCapturedInvoiceProjectionKafkaHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroPaymentCapturedEvent message)
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
        });

        var (row, isNew) = await PendingProjectionUpsertHelper.GetOrAddAsync(
            _db.PendingInvoices,
            message.CorrelationId,
            () => new PendingInvoice
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
                    "PaymentCapturedEvent already projected for CorrelationId {CorrelationId}; no-op.",
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
            "PaymentCapturedEvent projected (IsNew={IsNew}, Converged={Converged})",
            isNew,
            row.CompletedAtUtc is not null);
    }

    private static string SerializePayload(AvroPaymentCapturedEvent message)
    {
        // See OrderConfirmedInvoiceProjectionKafkaHandler.SerializePayload for the rationale
        // on the hand-rolled DTO. Avro.AvroDecimal needs explicit conversion — System.Text.Json
        // doesn't know about it; cast to decimal first (AvroDecimal exposes an explicit cast).
        return JsonSerializer.Serialize(new
        {
            message.CorrelationId,
            message.UserId,
            message.PaymentTransactionId,
            message.AuthorizationId,
            Amount = (decimal)message.Amount,
            message.Currency,
            message.CapturedAtUtc,
        });
    }
}
