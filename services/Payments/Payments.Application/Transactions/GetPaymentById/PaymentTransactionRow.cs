using System.Linq.Expressions;
using Payments.Application.Transactions.GetPaymentById;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;

namespace Payments.Application.Transactions;

/// <summary>
/// SQL-side projection of the <see cref="PaymentTransaction"/> aggregate carrying only the columns
/// the read DTO needs. Shared by <c>GetPaymentByIdQueryHandler</c> and
/// <c>GetPaymentsByOrderQueryHandler</c> (ADR-0021) so neither materialises the full aggregate
/// (owned-VO graph + value-converter round-trips). The wide forensic columns the DTO ignores —
/// <c>gateway_response_message</c>, <c>void_reason</c>, the <c>xmin</c> concurrency token — never
/// leave the database. Sensitive tokens are carried raw and masked to last-4 in
/// <see cref="ToResponse"/> per ADR-0011: the DB holds the full value; only the HTTP response is masked.
/// </summary>
/// <remarks>
/// <c>Status</c> and <c>FailureReason</c> are SmartEnums whose value converter stores the integer
/// <c>Value</c>, so they are projected as the converted objects (EF reads the column and applies the
/// converter) and stringified via <c>.Name</c> in <see cref="ToResponse"/>. <c>Currency</c>'s
/// converter stores the <c>Name</c>, so <c>tx.Amount.Currency.Name</c> maps straight to the column.
/// </remarks>
internal sealed record PaymentTransactionRow
{
    public required Guid PaymentId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string PaymentMethodId { get; init; }

    public required PaymentStatus Status { get; init; }

    public string? GatewayTransactionId { get; init; }

    public string? GatewayResponseCode { get; init; }

    public DateTimeOffset? AuthorizedAtUtc { get; init; }

    public DateTimeOffset? CapturedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public DateTimeOffset? VoidedAtUtc { get; init; }

    public DateTimeOffset? RefundedAtUtc { get; init; }

    public FailureReason? FailureReason { get; init; }

    public string? FailureGatewayCode { get; init; }

    public DateTimeOffset? FailureRecordedAtUtc { get; init; }

    public static Expression<Func<PaymentTransaction, PaymentTransactionRow>> Projection => tx =>
        new PaymentTransactionRow
        {
            PaymentId = tx.Id,
            BuyerId = tx.BuyerId,
            OrderId = tx.OrderId,
            Amount = tx.Amount.Amount,
            Currency = tx.Amount.Currency.Name,
            PaymentMethodId = tx.PaymentMethodId.Value,
            Status = tx.Status,
            GatewayTransactionId = tx.GatewayTransactionId,
            GatewayResponseCode = tx.GatewayResponseCode == null ? null : tx.GatewayResponseCode.Code,
            AuthorizedAtUtc = tx.AuthorizedAtUtc,
            CapturedAtUtc = tx.CapturedAtUtc,
            CompletedAtUtc = tx.CompletedAtUtc,
            VoidedAtUtc = tx.VoidedAtUtc,
            RefundedAtUtc = tx.RefundedAtUtc,
            FailureReason = tx.FailureInfo == null ? null : tx.FailureInfo.Reason,
            FailureGatewayCode = tx.FailureInfo == null ? null : tx.FailureInfo.GatewayCode,
            FailureRecordedAtUtc = tx.FailureInfo == null ? (DateTimeOffset?)null : tx.FailureInfo.RecordedAtUtc,
        };

    public GetPaymentByIdResponse ToResponse() =>
        new()
        {
            PaymentId = PaymentId,
            BuyerId = BuyerId,
            OrderId = OrderId,
            Amount = Amount,
            Currency = Currency,
            PaymentMethodId = MaskTrailing(PaymentMethodId),
            Status = Status.Name,
            GatewayTransactionId = GatewayTransactionId is null ? null : MaskTrailing(GatewayTransactionId),
            GatewayResponseCode = GatewayResponseCode,
            AuthorizedAtUtc = AuthorizedAtUtc,
            CapturedAtUtc = CapturedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            VoidedAtUtc = VoidedAtUtc,
            RefundedAtUtc = RefundedAtUtc,
            FailureInfo = FailureReason is null ? null : new FailureInfoDto
            {
                Reason = FailureReason.Name,
                GatewayCode = FailureGatewayCode,
                RecordedAtUtc = FailureRecordedAtUtc!.Value,
            },
        };

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
