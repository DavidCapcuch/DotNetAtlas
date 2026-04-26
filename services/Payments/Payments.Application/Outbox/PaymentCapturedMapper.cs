using Payments.Domain.Transactions.Events;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentCapturedDomainEvent"/> to the external Avro
/// <see cref="PaymentCapturedEvent"/>. Field renames per Path B: <c>BuyerId</c> →
/// <c>UserId</c>; aggregate <c>Id</c> → <c>PaymentTransactionId</c>; <c>GatewayTransactionId</c>
/// → <c>AuthorizationId</c>. Consumed by Invoicing for invoice issuance and by
/// PaymentProcessingSaga for capture-success confirmation.
/// </summary>
internal static class PaymentCapturedMapper
{
    private const int DecimalScale = 4;

    public static PaymentCapturedEvent ToPaymentCapturedEvent(this PaymentCapturedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentCapturedEvent
        {
            CorrelationId = source.CorrelationId,
            UserId = source.BuyerId,
            PaymentTransactionId = source.PaymentId,
            AuthorizationId = source.GatewayTransactionId,
            Amount = source.Amount.Amount.ToAvroDecimal(DecimalScale),
            Currency = source.Amount.Currency.Name,
            CapturedAtUtc = source.CapturedAtUtc.UtcDateTime,
        };
    }
}
