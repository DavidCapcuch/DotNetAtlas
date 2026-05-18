using Payments.Application.Abstractions;
using Payments.Domain.Transactions.Events;
using Payments.Transactions;

namespace Payments.Application.Outbox;

/// <summary>
/// Maps <see cref="PaymentCaptureFailedDomainEvent"/> to the external Avro
/// <see cref="PaymentCaptureFailedEvent"/>. <c>AuthorizationId</c> is non-null because the
/// aggregate guarantees a successful prior <c>Authorize</c> before capture (FSM source-state
/// guard in <c>PaymentTransaction.MarkCaptureFailed</c>). Same <c>ErrorCode</c> /
/// <c>ErrorMessage</c> / <c>IsRetryable</c> resolution as
/// <see cref="PaymentAuthorizationFailedMapper"/>.
/// </summary>
internal static class PaymentCaptureFailedMapper
{
    public static PaymentCaptureFailedEvent ToPaymentCaptureFailedEvent(
        this PaymentCaptureFailedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PaymentCaptureFailedEvent
        {
            CorrelationId = source.CorrelationId,
            UserId = source.BuyerId,
            AuthorizationId = source.GatewayTransactionId,
            ErrorCode = source.FailureInfo.GatewayCode ?? source.FailureInfo.Reason.Name,
            ErrorMessage = source.FailureInfo.Reason.Name,
            IsRetryable = GatewayResponseClassifier.IsRetryable(source.FailureInfo.Reason),
            FailedAtUtc = source.FailureInfo.RecordedAtUtc.UtcDateTime,
        };
    }
}
