using Payments.Domain.Transactions.Events;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentRefundedDomainEvent"/> to the external Avro
/// <see cref="PaymentRefundedEvent"/>. Consumed by Checkout saga (cancel-post-capture
/// confirmation), Notifications, and Invoicing (credit-note trigger).
/// </summary>
/// <remarks>
/// <para>
/// <b>RefundTransactionId is a fresh UUID v7</b> — distinct from the originating
/// <c>PaymentTransactionId</c> per #246 (downstream consumers key off
/// <c>RefundTransactionId</c> as a distinct value for reconciliation-by-id). Today's v1
/// aggregate carries no refund-row concept, so the mapper itself generates the identifier
/// at projection time. When partial / multiple refunds enter the aggregate model (v2), the
/// identifier will move onto the refund row and this generator becomes obsolete.
/// </para>
/// </remarks>
internal static class PaymentRefundedMapper
{
    private const int DecimalScale = 4;

    public static PaymentRefundedEvent ToPaymentRefundedEvent(this PaymentRefundedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentRefundedEvent
        {
            UserId = source.BuyerId,
            PaymentTransactionId = source.PaymentId,
            // #246: fresh UUID v7 per refund-row (no aggregate change in v1; v2 partial-refund
            // model will own the identifier on the refund entity). Distinct from
            // PaymentTransactionId so downstream consumers (Notifications refund email,
            // Invoicing credit-note pairing) can key off it without collision.
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAmount = source.Amount.Amount.ToAvroDecimal(DecimalScale),
            Currency = source.Amount.Currency.Name,
            RefundedAtUtc = source.RefundedAtUtc.UtcDateTime,
        };
    }
}
