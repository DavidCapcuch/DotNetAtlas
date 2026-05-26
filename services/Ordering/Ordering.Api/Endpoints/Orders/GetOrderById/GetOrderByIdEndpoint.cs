using System.Net;
using FastEndpoints;
using Ordering.Api.Common.Extensions;
using Ordering.Application.Orders.GetOrderById;
using Serilog.Context;

namespace Ordering.Api.Endpoints.Orders.GetOrderById;

/// <summary>
/// <c>GET /api/v1/ordering/orders/{orderId}</c> — single-order read endpoint.
/// Authorisation enforced inside the query handler: a buyer requesting an
/// order owned by a different buyer surfaces as 404 (not 403) to avoid
/// leaking existence; admins read any order.
/// </summary>
internal sealed class GetOrderByIdEndpoint
    : Endpoint<GetOrderByIdRequest, GetOrderByIdResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetOrderByIdQuery, GetOrderByIdResponse> _handler;

    public GetOrderByIdEndpoint(
        Platform.CQRS.IQueryHandler<GetOrderByIdQuery, GetOrderByIdResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("{OrderId}");
        Version(1);
        Group<OrdersGroup>();
        Summary(s =>
        {
            s.Summary = "Get an order by id (own order for buyer; any for admin).";
            s.ExampleRequest = new GetOrderByIdRequest
            {
                OrderId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
            };
        });
        Description(b =>
        {
            b.Produces<GetOrderByIdResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Unauthorized);
        });
    }

    public override async Task HandleAsync(GetOrderByIdRequest req, CancellationToken ct)
    {
        var buyerId = User.GetBuyerIdOrNull();
        var isAdmin = User.IsOrderingAdmin();

        if (!isAdmin && buyerId is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        using var _ = LogContext.PushProperty("OrderId", req.OrderId);
        using var __ = LogContext.PushProperty("IsAdmin", isAdmin);

        var query = new GetOrderByIdQuery
        {
            OrderId = req.OrderId,
            BuyerId = buyerId ?? Guid.Empty,
            IsAdmin = isAdmin,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
