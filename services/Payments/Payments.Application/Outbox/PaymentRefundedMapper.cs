using Payments.Domain.Transactions.Events;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentRefundedDomainEvent"/> to the external Avro
/// <see cref="PaymentRefundedEvent"/>. v1 has no separate refund aggregate, so
/// <c>RefundTransactionId</c> reuses the original <c>PaymentTransactionId</c> — flagged in
/// the M4 session summary as a follow-up when partial / multiple refunds enter the model.
/// Consumed by Checkout saga (cancel-post-capture confirmation), Notifications, and
/// Invoicing (credit-note trigger).
/// </summary>
internal static class PaymentRefundedMapper
{
    private const int DecimalScale = 4;

    public static PaymentRefundedEvent ToPaymentRefundedEvent(this PaymentRefundedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentRefundedEvent
        {
            CorrelationId = source.CorrelationId,
            UserId = source.BuyerId,
            PaymentTransactionId = source.PaymentId,
            // v1 placeholder: aggregate has one Id; full refund == one row. Replace when partial
            // refunds land (issue tracker: payments-bc).
            RefundTransactionId = source.PaymentId,
            RefundedAmount = source.Amount.Amount.ToAvroDecimal(DecimalScale),
            Currency = source.Amount.Currency.Name,
            RefundedAtUtc = source.RefundedAtUtc.UtcDateTime,
        };
    }
}
