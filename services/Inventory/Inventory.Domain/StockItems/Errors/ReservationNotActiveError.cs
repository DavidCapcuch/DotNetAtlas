using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Errors;

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
/// <remarks>
/// Inherits <see cref="ConflictError"/> so the canonical
/// <c>Platform.Api.Extensions</c> dispatch maps it to 409 without a BC-specific
/// case. Modelled as a sealed class (not a record) because C# records cannot
/// inherit from non-record bases (CS8864).
/// </remarks>
public sealed class ReservationNotActiveError(
    Guid productId,
    Guid reservationId,
    ReservationStatus currentStatus)
    : ConflictError(
        entityName: "Reservation",
        message: FormattableString.Invariant($"Reservation {reservationId} on stock item {productId} is not Active (current status: {currentStatus})."),
        errorCode: "Inventory.ReservationNotActive")
{
    public Guid ProductId { get; } = productId;

    public Guid ReservationId { get; } = reservationId;

    public ReservationStatus CurrentStatus { get; } = currentStatus;
}
