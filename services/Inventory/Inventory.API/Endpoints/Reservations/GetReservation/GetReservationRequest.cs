using FastEndpoints;

namespace Inventory.API.Endpoints.Reservations.GetReservation;

internal sealed class GetReservationRequest
{
    [BindFrom("reservationId")]
    public required Guid ReservationId { get; init; }
}
