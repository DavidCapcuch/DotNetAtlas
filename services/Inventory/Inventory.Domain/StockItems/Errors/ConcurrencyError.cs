using Platform.SharedKernel.Errors;

namespace Inventory.Domain.StockItems.Errors;

/// <summary>
/// Returned by the event-store repository after the retry-once policy is exhausted —
/// another writer appended the next version while this handler was rehydrating.
/// </summary>
/// <remarks>
/// Inherits <see cref="ConflictError"/> so the canonical
/// <c>Platform.Api.Extensions</c> dispatch maps it to 409 without a BC-specific
/// case. Modelled as a sealed class (not a record) because C# records cannot
/// inherit from non-record bases (CS8864). Shape is locked by
/// <c>docs/bc-design/error-taxonomy.md</c> § 3.4.
/// </remarks>
public sealed class ConcurrencyError(Guid streamId, int expectedVersion)
    : ConflictError(
        entityName: "StockItem",
        message: FormattableString.Invariant($"Stream {streamId} version conflict at {expectedVersion}."),
        errorCode: "Inventory.Concurrency")
{
    public Guid StreamId { get; } = streamId;

    public int ExpectedVersion { get; } = expectedVersion;
}
