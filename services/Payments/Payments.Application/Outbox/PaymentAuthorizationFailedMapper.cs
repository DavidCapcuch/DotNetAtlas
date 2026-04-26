using Payments.Domain.Transactions.Events;
using Payments.Transactions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentAuthorizationFailedDomainEvent"/> to the external Avro
/// <see cref="PaymentAuthorizationFailedEvent"/>. <c>ErrorCode</c> prefers the raw gateway code
/// (e.g. <c>"insufficient_funds"</c>) when available, otherwise falls back to the canonical
/// <see cref="Payments.Domain.Transactions.ValueObjects.FailureReason"/> name. <c>ErrorMessage</c>
/// always carries the canonical reason name as a stable, taxonomy-aligned token; downstream
/// sagas / dashboards bucket failures by <c>ErrorCode</c> first and fall back to
/// <c>ErrorMessage</c> for unknown codes.
/// </summary>
internal static class PaymentAuthorizationFailedMapper
{
    public static PaymentAuthorizationFailedEvent ToPaymentAuthorizationFailedEvent(
        this PaymentAuthorizationFailedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentAuthorizationFailedEvent
        {
            CorrelationId = source.CorrelationId,
            UserId = source.BuyerId,
            ErrorCode = source.FailureInfo.GatewayCode ?? source.FailureInfo.Reason.Name,
            ErrorMessage = source.FailureInfo.Reason.Name,
            FailedAtUtc = source.FailureInfo.RecordedAtUtc.UtcDateTime,
        };
    }
}
