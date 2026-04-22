using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Ordering.Domain.Orders.ValueObjects;

/// <summary>
/// Set on <see cref="Order.Cancellation"/> by <c>Order.Cancel</c>. Captures the
/// caller-supplied reason, the status at cancellation time (drives compensation
/// routing downstream), and the cancellation timestamp.
/// </summary>
public sealed record CancellationInfo : ValueObject
{
    public const int MaxReasonLength = 500;

    public string Reason { get; private init; } = string.Empty;
    public OrderStatus AtStatus { get; private init; } = null!;
    public DateTimeOffset CancelledAtUtc { get; private init; }

    private CancellationInfo()
    {
    }

    public static Result<CancellationInfo> Create(string? reason, OrderStatus atStatus, DateTimeOffset cancelledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(atStatus);

        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Fail(new ValidationError(
                nameof(Reason), "Cancellation reason must not be empty.", "CancellationInfo.ReasonEmpty"));
        }

        if (trimmed.Length > MaxReasonLength)
        {
            return Result.Fail(new ValidationError(
                nameof(Reason),
                $"Cancellation reason must not exceed {MaxReasonLength} characters.",
                "CancellationInfo.ReasonTooLong"));
        }

        return new CancellationInfo
        {
            Reason = trimmed,
            AtStatus = atStatus,
            CancelledAtUtc = cancelledAtUtc,
        };
    }
}
