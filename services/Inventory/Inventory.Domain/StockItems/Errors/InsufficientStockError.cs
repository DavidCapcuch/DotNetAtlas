using System.Globalization;
using FluentResults;

namespace Inventory.Domain.StockItems.Errors;

/// <summary>
/// Business-expected outcome of <c>ReserveStockCommand</c> when <c>Available</c> is
/// less than the requested <c>Quantity</c>. Never thrown — flows as
/// <see cref="Result.Fail(FluentResults.IError)"/> and is translated by the application-layer
/// handler into an external <c>StockReservationFailedEvent</c>.
/// </summary>
/// <remarks>
/// Shape is locked by <c>docs/bc-design/error-taxonomy.md</c> § 3.4.
/// </remarks>
public sealed record InsufficientStockError(Guid ProductId, int Requested, int Available) : IError
{
    public string Message =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Stock item {0}: requested {1}, available {2}.",
            ProductId,
            Requested,
            Available);

    public Dictionary<string, object> Metadata { get; } = new()
    {
        ["ErrorCode"] = "Inventory.InsufficientStock",
        ["ProductId"] = ProductId,
        ["Requested"] = Requested,
        ["Available"] = Available,
    };

    public List<IError> Reasons { get; } = [];
}
