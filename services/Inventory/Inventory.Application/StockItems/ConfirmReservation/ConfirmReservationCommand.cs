using Platform.CQRS;

namespace Inventory.Application.StockItems.ConfirmReservation;

/// <summary>
/// Finalises a reservation — stock physically leaves the warehouse. Issued by
/// the Checkout saga after payment capture. Idempotent on
/// <see cref="ReservationId"/>: a second confirm on an already-<c>Confirmed</c>
/// reservation is <c>Result.Ok</c> with no event.
/// </summary>
public sealed record ConfirmReservationCommand : ICommand
{
    public required Guid ReservationId { get; init; }

    public required Guid ProductId { get; init; }

    public required DateTimeOffset OccurredOnUtc { get; init; }

    public Guid? CorrelationId { get; init; }
}
