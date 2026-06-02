using Payments.Domain.Transactions.Events;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentCompletedDomainEvent"/> to the external Avro
/// <see cref="PaymentCompletedEvent"/> on <c>payments.transactions</c>. Field renames per Path B:
/// <c>BuyerId</c> → <c>UserId</c>; aggregate <c>Id</c> → <c>PaymentTransactionId</c>. Per ADR-0026
/// the Payments service (not PaymentProcessingSaga) is the authoritative producer of the terminal
/// <c>PaymentCompletedEvent</c>; the Checkout saga consumes it to finalize the order.
/// </summary>
internal static class PaymentCompletedMapper
{
    private const int DecimalScale = 4;

    public static PaymentCompletedEvent ToPaymentCompletedEvent(this PaymentCompletedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentCompletedEvent
        {
            CorrelationId = source.CorrelationId,
            UserId = source.BuyerId,
            PaymentTransactionId = source.PaymentId,
            Amount = source.Amount.Amount.ToAvroDecimal(DecimalScale),
            Currency = source.Amount.Currency.Name,
            CompletedAtUtc = source.CompletedAtUtc.UtcDateTime,
        };
    }
}
