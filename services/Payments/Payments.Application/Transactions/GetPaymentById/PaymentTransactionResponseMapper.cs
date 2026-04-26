using Payments.Application.Transactions.GetPaymentById;
using Payments.Domain.Transactions;

namespace Payments.Application.Transactions;

/// <summary>
/// Shared aggregate → <see cref="GetPaymentByIdResponse"/> projection. Used by both
/// <c>GetPaymentByIdQueryHandler</c> and <c>GetPaymentsByOrderQueryHandler</c> so the wire
/// shape is identical.
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
            PaymentMethodId = tx.PaymentMethodId.Value,
            Status = tx.Status.Name,
            GatewayTransactionId = tx.GatewayTransactionId,
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
}
