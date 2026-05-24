using Platform.SharedKernel.Base;

namespace Payments.Domain.Transactions.ValueObjects;

/// <summary>
/// Terminal-failure details attached to a <see cref="PaymentTransaction"/> when it reaches
/// <see cref="PaymentStatus.Failed"/>. Persistence shape (owned entity vs JSON column) is decided
/// at the Infrastructure milestone (M5); the domain treats this as a plain value object.
/// </summary>
public sealed record FailureInfo : ValueObject
{
    /// <summary>Classified reason the transaction failed.</summary>
    public FailureReason Reason { get; private init; } = null!;

    /// <summary>Raw gateway code for the failure (null if not available).</summary>
    public string? GatewayCode { get; private init; }

    /// <summary>When the failure was observed and recorded on the aggregate.</summary>
    public DateTimeOffset RecordedAtUtc { get; private init; }

    private FailureInfo()
    {
    }

    public static FailureInfo Create(FailureReason reason, string? gatewayCode, DateTimeOffset recordedAtUtc) =>
        new()
        {
            Reason = reason,
            GatewayCode = gatewayCode,
            RecordedAtUtc = recordedAtUtc,
        };
}
