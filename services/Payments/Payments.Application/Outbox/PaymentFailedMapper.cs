using Payments.Domain.Transactions.Events;
using Payments.Transactions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentFailedDomainEvent"/> to the external Avro <see cref="PaymentFailedEvent"/>
/// on <c>payments.transactions</c>. <c>ErrorCode</c> prefers the raw gateway code (e.g.
/// <c>"card_declined"</c>) when available, otherwise falls back to the canonical
/// <see cref="Payments.Domain.Transactions.ValueObjects.FailureReason"/> name; <c>ErrorMessage</c>
/// always carries the canonical reason name — symmetric with
/// <see cref="PaymentAuthorizationFailedMapper"/>. Per ADR-0026 the Payments service (not
/// PaymentProcessingSaga) is the authoritative producer of the terminal <c>PaymentFailedEvent</c>,
/// co-raised on both <c>MarkAuthorizationFailed</c> and <c>MarkCaptureFailed</c>; the Checkout saga
/// consumes it to fast-fail (release the reservation) without waiting out the payment timeout.
/// </summary>
internal static class PaymentFailedMapper
{
    public static PaymentFailedEvent ToPaymentFailedEvent(this PaymentFailedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentFailedEvent
        {
            CorrelationId = source.CorrelationId,
            UserId = source.BuyerId,
            ErrorCode = source.FailureInfo.GatewayCode ?? source.FailureInfo.Reason.Name,
            ErrorMessage = source.FailureInfo.Reason.Name,
            FailedAtUtc = source.FailedAtUtc.UtcDateTime,
        };
    }
}
