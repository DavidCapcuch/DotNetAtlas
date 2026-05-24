using Payments.Application.Transactions.GetPaymentById;
using Payments.Domain.Transactions;

namespace Payments.Application.Transactions;

/// <summary>
/// Shared aggregate → <see cref="GetPaymentByIdResponse"/> projection. Used by both
/// <c>GetPaymentByIdQueryHandler</c> and <c>GetPaymentsByOrderQueryHandler</c> so the wire
/// shape is identical. Sensitive token fields are masked per ADR-0011 (see
/// <see cref="MaskTrailing"/>); the underlying <c>*_enc</c> columns hold the full value
/// for the BCs that legitimately need it (Invoicing's AuthorizationId, outbox events).
/// </summary>
internal static class PaymentTransactionResponseMapper
{
    public static GetPaymentByIdResponse ToResponse(this PaymentTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        return new GetPaymentByIdResponse
        {
            PaymentId = tx.Id,
            CorrelationId = tx.CorrelationId,
            BuyerId = tx.BuyerId,
            OrderId = tx.OrderId,
            Amount = tx.Amount.Amount,
            Currency = tx.Amount.Currency.Name,
            PaymentMethodId = MaskTrailing(tx.PaymentMethodId.Value),
            Status = tx.Status.Name,
            GatewayTransactionId = tx.GatewayTransactionId is null ? null : MaskTrailing(tx.GatewayTransactionId),
            GatewayResponseCode = tx.GatewayResponseCode?.Code,
            AuthorizedAtUtc = tx.AuthorizedAtUtc,
            CapturedAtUtc = tx.CapturedAtUtc,
            CompletedAtUtc = tx.CompletedAtUtc,
            VoidedAtUtc = tx.VoidedAtUtc,
            RefundedAtUtc = tx.RefundedAtUtc,
            FailureInfo = tx.FailureInfo is null ? null : new FailureInfoDto
            {
                Reason = tx.FailureInfo.Reason.Name,
                GatewayCode = tx.FailureInfo.GatewayCode,
                RecordedAtUtc = tx.FailureInfo.RecordedAtUtc,
            },
        };
    }

    /// <summary>
    /// Masks all but the trailing 4 characters of a sensitive token (ADR-0011).
    /// Values of 4 characters or fewer are returned as the literal <c>"***"</c> so
    /// no fingerprint of a short token leaks. Returns the empty string unchanged.
    /// </summary>
    internal static string MaskTrailing(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 4 ? "***" : $"****{value[^4..]}";
    }
}
