using FastEndpoints;

namespace Inventory.Api.Endpoints.Reservations.GetReservation;

internal sealed class GetReservationRequest
{
    [BindFrom("reservationId")]
    public required Guid ReservationId { get; init; }
}
