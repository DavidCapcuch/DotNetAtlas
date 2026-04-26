using Payments.Domain.Transactions.Events;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentAuthorizedDomainEvent"/> to the external Avro
/// <see cref="PaymentAuthorizedEvent"/>. Hand-written because the field-set diverges from the
/// domain event (<c>BuyerId</c> → <c>UserId</c>; <c>GatewayTransactionId</c> →
/// <c>AuthorizationId</c>; <c>OrderId</c> dropped — recoverable downstream via
/// <c>CorrelationId</c> per Path B in the M4 plan), and the Avro schema requires an
/// <c>ExpiresAtUtc</c> sentinel the aggregate has no notion of (v1 placeholder; configurable
/// in v2 once the gateway exposes a real expiry).
/// </summary>
internal static class PaymentAuthorizedMapper
{
    private const int DecimalScale = 4;

    /// <summary>
    /// Sentinel authorization-expiry window. Real gateways return this value on the authorize
    /// response; the v1 stub does not, so M4 emits <c>AuthorizedAtUtc + 7 days</c>. Documented
    /// in the M4 session summary as a follow-up for M5+ when a real adapter ships.
    /// </summary>
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromDays(7);

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
            ExpiresAtUtc = source.AuthorizedAtUtc.Add(AuthorizationLifetime).UtcDateTime,
        };
    }
}
