using FluentResults;
using Platform.SharedKernel.Errors;

namespace Inventory.Domain.StockItems.Errors;

/// <summary>
/// Business-expected outcome of <c>ReserveStockCommand</c> when <c>Available</c> is
/// less than the requested <c>Quantity</c>. Never thrown — flows as
/// <see cref="Result.Fail(FluentResults.IError)"/> and is translated by the
/// application-layer handler into an external <c>StockReservationFailedEvent</c>.
/// </summary>
/// <remarks>
/// Inherits <see cref="ConflictError"/> so the canonical
/// <c>Platform.Api.Extensions</c> dispatch maps it to 409 without a BC-specific
/// case. Modelled as a sealed class (not a record) because C# records cannot
/// inherit from non-record bases (CS8864). Shape is locked by
/// <c>docs/bc-design/error-taxonomy.md</c> § 3.4.
/// </remarks>
public sealed class InsufficientStockError(Guid productId, int requested, int available)
    : ConflictError(
        entityName: "StockItem",
        message: FormattableString.Invariant($"Stock item {productId}: requested {requested}, available {available}."),
        errorCode: "Inventory.InsufficientStock")
{
    public Guid ProductId { get; } = productId;

    public int Requested { get; } = requested;

    public int Available { get; } = available;
}
