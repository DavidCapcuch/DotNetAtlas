namespace Payments.Application.Transactions.GetPaymentById;

/// <summary>
/// Admin-facing read DTO for a single <see cref="Payments.Domain.Transactions.PaymentTransaction"/>.
/// Sensitive token fields (<c>PaymentMethodId</c>, <c>GatewayTransactionId</c>) are masked
/// to last-4 in the response per ADR-0011 (see
/// <see cref="PaymentTransactionResponseMapper"/>). Underlying <c>*_enc</c> columns remain
/// v1 plaintext on disk; full crypto-shredding lands with v2 DEK encryption.
/// </summary>
public sealed record GetPaymentByIdResponse
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string PaymentMethodId { get; init; }

    public required string Status { get; init; }

    public string? GatewayTransactionId { get; init; }

    public string? GatewayResponseCode { get; init; }

    public DateTimeOffset? AuthorizedAtUtc { get; init; }

    public DateTimeOffset? CapturedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public DateTimeOffset? VoidedAtUtc { get; init; }

    public DateTimeOffset? RefundedAtUtc { get; init; }

    public FailureInfoDto? FailureInfo { get; init; }
}

/// <summary>
/// Read-side projection of <see cref="Payments.Domain.Transactions.ValueObjects.FailureInfo"/>.
/// </summary>
public sealed record FailureInfoDto
{
    public required string Reason { get; init; }

    public string? GatewayCode { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }
}
