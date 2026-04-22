using FluentResults;

namespace Basket.Domain.Baskets.Errors;

/// <summary>
/// Raised when a Redis CAS save detects a <c>Basket.Version</c> mismatch.
/// Not a <see cref="Platform.SharedKernel.Errors.ValidationError"/> — concurrency
/// is a pipeline concern, and application-layer handlers retry exactly once on
/// this before surfacing a 409 to the caller (see basket.md § 5.4).
/// </summary>
public sealed record BasketConcurrencyError(Guid UserId, int Expected, int Actual) : IError
{
    public string Message => $"Basket '{UserId}' version conflict: expected {Expected}, found {Actual}.";

    public Dictionary<string, object> Metadata { get; } = new()
    {
        ["ErrorCode"] = "Basket.Concurrency",
    };

    public List<IError> Reasons { get; } = [];
}
