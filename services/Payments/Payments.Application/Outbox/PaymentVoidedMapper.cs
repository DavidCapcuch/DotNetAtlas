using Payments.Domain.Transactions.Events;
using Payments.Transactions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentVoidedDomainEvent"/> to the external Avro
/// <see cref="PaymentVoidedEvent"/>. Field renames per Path B: <c>BuyerId</c> →
/// <c>UserId</c>; <c>GatewayTransactionId</c> → <c>AuthorizationId</c>.
/// </summary>
internal static class PaymentVoidedMapper
{
    public static PaymentVoidedEvent ToPaymentVoidedEvent(this PaymentVoidedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentVoidedEvent
        {
            CorrelationId = source.CorrelationId,
            OrderId = source.OrderId,
            UserId = source.BuyerId,
            AuthorizationId = source.GatewayTransactionId,
            VoidedAtUtc = source.VoidedAtUtc.UtcDateTime,
            Reason = source.Reason,
        };
    }
}
