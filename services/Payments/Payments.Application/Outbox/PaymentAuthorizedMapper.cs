using Payments.Domain.Transactions.Events;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentAuthorizedDomainEvent"/> to the external Avro
/// <see cref="PaymentAuthorizedEvent"/>. Hand-written because the field-set diverges from the
/// domain event (<c>BuyerId</c> → <c>UserId</c>; <c>GatewayTransactionId</c> →
/// <c>AuthorizationId</c>; <c>OrderId</c> dropped — recoverable downstream via
/// <c>CorrelationId</c> per Path B in the M4 plan). <c>ExpiresAtUtc</c> is now sourced from
/// the gateway response (carried through the domain event); v1 stub returns
/// <c>now + 7 days</c>, real PSP adapters return the gateway's value (H-6 closeout).
/// </summary>
internal static class PaymentAuthorizedMapper
{
    private const int DecimalScale = 4;

    public static PaymentAuthorizedEvent ToPaymentAuthorizedEvent(this PaymentAuthorizedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentAuthorizedEvent
        {
            CorrelationId = source.CorrelationId,
            UserId = source.BuyerId,
            AuthorizationId = source.GatewayTransactionId,
            Amount = source.Amount.Amount.ToAvroDecimal(DecimalScale),
            Currency = source.Amount.Currency.Name,
            AuthorizedAtUtc = source.AuthorizedAtUtc.UtcDateTime,
            ExpiresAtUtc = source.ExpiresAtUtc.UtcDateTime,
        };
    }
}
