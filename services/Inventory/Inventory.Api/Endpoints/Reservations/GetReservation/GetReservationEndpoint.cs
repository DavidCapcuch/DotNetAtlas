using System.Net;
using FastEndpoints;
using Inventory.Api.Common.Authorization;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.GetReservationById;
using Platform.Api.Extensions;

namespace Inventory.Api.Endpoints.Reservations.GetReservation;

/// <summary>
/// Lookup endpoint for the <c>reservation_audit</c> projection. Reuses
/// <see cref="InventoryGroup"/> so the route is grouped under
/// <c>/api/v1/inventory/reservations/{reservationId}</c>.
/// </summary>
internal sealed class GetReservationEndpoint : Endpoint<GetReservationRequest, ReservationAuditResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetReservationByIdQuery, ReservationAuditResponse> _handler;

    public GetReservationEndpoint(
        Platform.CQRS.IQueryHandler<GetReservationByIdQuery, ReservationAuditResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("reservations/{reservationId:guid}");
        Version(1);
        Group<InventoryGroup>();
        Policies(AuthPolicies.ReadPolicy);
        Summary(s => s.Summary = "Returns the reservation_audit row for a ReservationId.");
        Description(b =>
        {
            b.Produces<ReservationAuditResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
        });
    }

    public override async Task HandleAsync(GetReservationRequest request, CancellationToken ct)
    {
        var query = new GetReservationByIdQuery { ReservationId = request.ReservationId };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
