using System.Globalization;
using FluentResults;
using Inventory.Domain.StockItems.ValueObjects;

namespace Inventory.Domain.StockItems.Errors;

/// <summary>
/// Returned by <c>ConfirmReservation</c> / <c>ReleaseReservation</c> when the
/// reservation exists on the stream but its current status is terminal and does
/// not match the command — confirming a released reservation, or releasing a
/// confirmed reservation. Shape follows <c>docs/bc-design/error-taxonomy.md</c>
/// § 3.4; type added beyond the two listed there because
/// <c>docs/bc-design/example-mapping/inventory.md</c> Sessions 1 and 3 specify a
/// <c>Result.Fail</c> (not a throw) for the terminal-status case.
/// </summary>
public sealed record ReservationNotActiveError(
    Guid ProductId,
    Guid ReservationId,
    ReservationStatus CurrentStatus) : IError
{
    public string Message =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Reservation {0} on stock item {1} is not Active (current status: {2}).",
            ReservationId,
            ProductId,
            CurrentStatus);

    public Dictionary<string, object> Metadata { get; } = new()
    {
        ["ErrorCode"] = "Inventory.ReservationNotActive",
        ["ProductId"] = ProductId,
        ["ReservationId"] = ReservationId,
        ["CurrentStatus"] = CurrentStatus,
    };

    public List<IError> Reasons { get; } = [];
}
