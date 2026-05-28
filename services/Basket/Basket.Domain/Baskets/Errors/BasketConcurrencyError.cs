using Platform.SharedKernel.Errors;

namespace Basket.Domain.Baskets.Errors;

/// <summary>
/// Raised when a Redis CAS save detects a <c>Basket.Version</c> mismatch.
/// Application-layer handlers retry exactly once on this before surfacing
/// a 409 to the caller (see basket.md § 5.4). Inherits
/// <see cref="ConflictError"/> so the canonical type-switch dispatch in
/// <c>Platform.Api.Extensions.ResponseSenderExtensions</c> maps it to 409
/// without a BC-specific case. Modelled as a sealed class (not a record)
/// because C# records cannot inherit from non-record bases (CS8864).
/// </summary>
public sealed class BasketConcurrencyError(Guid userId, int expected, int actual)
    : ConflictError(
        entityName: "Basket",
        message: $"Basket '{userId}' version conflict: expected {expected}, found {actual}.",
        errorCode: "Basket.Concurrency")
{
    public Guid UserId { get; } = userId;

    public int Expected { get; } = expected;

    public int Actual { get; } = actual;
}
