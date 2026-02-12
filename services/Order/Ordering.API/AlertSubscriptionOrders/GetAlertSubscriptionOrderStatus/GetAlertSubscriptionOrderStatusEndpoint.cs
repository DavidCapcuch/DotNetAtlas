using System.Net;
using DotNetAtlas.CQS;
using FastEndpoints;
using Ordering.API.Common.Extensions;
using Ordering.Application.AlertSubscriptions.GetAlertSubscriptionOrderStatus;
using Serilog.Context;

namespace Ordering.API.AlertSubscriptionOrders.GetAlertSubscriptionOrderStatus;

internal sealed class GetAlertSubscriptionOrderStatusEndpoint :
    Endpoint<GetAlertSubscriptionOrderStatusQuery, GetAlertSubscriptionOrderStatusResponse>
{
    private readonly
        IQueryHandler<GetAlertSubscriptionOrderStatusQuery, GetAlertSubscriptionOrderStatusResponse>
        _getAlertSubscriptionOrderStatusQueryHandler;

    public GetAlertSubscriptionOrderStatusEndpoint(
        IQueryHandler<GetAlertSubscriptionOrderStatusQuery, GetAlertSubscriptionOrderStatusResponse>
            getAlertSubscriptionOrderStatusQueryHandler)
    {
        _getAlertSubscriptionOrderStatusQueryHandler = getAlertSubscriptionOrderStatusQueryHandler;
    }

    public override void Configure()
    {
        Get("status/{id}");
        Version(1);
        Group<AlertSubscriptionOrdersGroup>();
        Summary(s =>
        {
            s.Summary = "Returns alert subscription order status by ID.";
            s.ExampleRequest =
                new GetAlertSubscriptionOrderStatusQuery
                {
                    Id = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC") // from deterministic seed test data
                };
        });
        Description(b => b.Produces((int)HttpStatusCode.NotFound));
    }

    public override async Task HandleAsync(
        GetAlertSubscriptionOrderStatusQuery query,
        CancellationToken ct)
    {
        using var _ = LogContext.PushProperty("OrderId", query.Id.ToString());

        var getAlertSubscriptionOrderStatusResult =
            await _getAlertSubscriptionOrderStatusQueryHandler.HandleAsync(query, ct);

        await getAlertSubscriptionOrderStatusResult.MatchAsync(
            orderResponse => Send.OkAsync(orderResponse, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
