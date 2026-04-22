using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Ordering.Domain.Orders.ValueObjects;

/// <summary>
/// Set on <see cref="Order.Failure"/> by <c>Order.Fail</c>. Carries the saga-assigned
/// error code (e.g. <c>PAYMENT_FAILED</c>, <c>STOCK_UNAVAILABLE</c>) and message so
/// downstream consumers (Notifications, BFF) can route and display without
/// round-tripping to Ordering.
/// </summary>
public sealed record FailureInfo : ValueObject
{
    public const int MaxErrorCodeLength = 100;
    public const int MaxErrorMessageLength = 1000;

    public string ErrorCode { get; private init; } = string.Empty;
    public string ErrorMessage { get; private init; } = string.Empty;
    public OrderStatus AtStatus { get; private init; } = null!;
    public DateTimeOffset FailedAtUtc { get; private init; }

    private FailureInfo()
    {
    }

    public static Result<FailureInfo> Create(
        string? errorCode,
        string? errorMessage,
        OrderStatus atStatus,
        DateTimeOffset failedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(atStatus);

        var trimmedCode = errorCode?.Trim() ?? string.Empty;
        if (trimmedCode.Length == 0)
        {
            return Result.Fail(new ValidationError(
                nameof(ErrorCode), "Error code must not be empty.", "FailureInfo.ErrorCodeEmpty"));
        }

        if (trimmedCode.Length > MaxErrorCodeLength)
        {
            return Result.Fail(new ValidationError(
                nameof(ErrorCode),
                $"Error code must not exceed {MaxErrorCodeLength} characters.",
                "FailureInfo.ErrorCodeTooLong"));
        }

        var trimmedMessage = errorMessage?.Trim() ?? string.Empty;
        if (trimmedMessage.Length == 0)
        {
            return Result.Fail(new ValidationError(
                nameof(ErrorMessage), "Error message must not be empty.", "FailureInfo.ErrorMessageEmpty"));
        }

        if (trimmedMessage.Length > MaxErrorMessageLength)
        {
            return Result.Fail(new ValidationError(
                nameof(ErrorMessage),
                $"Error message must not exceed {MaxErrorMessageLength} characters.",
                "FailureInfo.ErrorMessageTooLong"));
        }

        return new FailureInfo
        {
            ErrorCode = trimmedCode,
            ErrorMessage = trimmedMessage,
            AtStatus = atStatus,
            FailedAtUtc = failedAtUtc,
        };
    }
}
