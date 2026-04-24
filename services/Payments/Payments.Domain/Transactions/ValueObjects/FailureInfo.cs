using Platform.SharedKernel.Base;

namespace Payments.Domain.Transactions.ValueObjects;

/// <summary>
/// Terminal-failure details attached to a <see cref="PaymentTransaction"/> when it reaches
/// <see cref="PaymentStatus.Failed"/>. Persistence shape (owned entity vs JSON column) is decided
/// at the Infrastructure milestone (M5); the domain treats this as a plain value object.
/// </summary>
/// <param name="Reason">Classified reason the transaction failed.</param>
/// <param name="GatewayCode">Raw gateway code for the failure (null if not available).</param>
/// <param name="RecordedAtUtc">When the failure was observed and recorded on the aggregate.</param>
public sealed record FailureInfo(
    FailureReason Reason,
    string? GatewayCode,
    DateTimeOffset RecordedAtUtc) : ValueObject;
