using System.Net;
using FastEndpoints;
using Ordering.Api.Common.Extensions;
using Ordering.Application.Orders.GetOrdersByBuyer;
using Platform.Api.Extensions;
using Serilog.Context;

namespace Ordering.Api.Endpoints.Orders.GetOrdersByBuyer;

/// <summary>
/// <c>GET /api/v1/ordering/orders?status=&amp;pageNumber=&amp;pageSize=</c> —
/// paged list of the calling buyer's orders. Admin override (an admin
/// requesting a particular buyer's orders) is deferred to v2+ per
/// <c>ordering.md Appendix B</c>.
/// </summary>
internal sealed class GetOrdersByBuyerEndpoint
    : Endpoint<GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetOrdersByBuyerQuery, GetOrdersByBuyerResponse> _handler;

    public GetOrdersByBuyerEndpoint(
        Platform.CQRS.IQueryHandler<GetOrdersByBuyerQuery, GetOrdersByBuyerResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get(string.Empty);
        Version(1);
        Group<OrdersGroup>();
        Summary(s =>
        {
            s.Summary = "List the calling buyer's orders, most recent first.";
        });
        Description(b =>
        {
            b.Produces<GetOrdersByBuyerResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(GetOrdersByBuyerRequest req, CancellationToken ct)
    {
        var buyerId = User.GetBuyerIdOrNull();
        if (buyerId is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        using var _ = LogContext.PushProperty("BuyerId", buyerId.Value);

        var query = new GetOrdersByBuyerQuery
        {
            BuyerId = buyerId.Value,
            Status = req.Status,
            PageNumber = req.PageNumber,
            PageSize = req.PageSize,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
