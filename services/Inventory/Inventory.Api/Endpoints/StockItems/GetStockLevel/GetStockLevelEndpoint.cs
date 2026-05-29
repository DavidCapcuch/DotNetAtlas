using System.Net;
using FastEndpoints;
using Inventory.Api.Common.Authorization;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.GetStockLevelByProductId;
using Platform.Api.Extensions;

namespace Inventory.Api.Endpoints.StockItems.GetStockLevel;

internal sealed class GetStockLevelEndpoint : Endpoint<GetStockLevelRequest, StockLevelResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetStockLevelByProductIdQuery, StockLevelResponse> _handler;

    public GetStockLevelEndpoint(
        Platform.CQRS.IQueryHandler<GetStockLevelByProductIdQuery, StockLevelResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("stock-items/{productId:guid}");
        Version(1);
        Group<InventoryGroup>();
        Policies(AuthPolicies.ReadPolicy);
        Summary(s => s.Summary = "Returns the current stock-level snapshot for a ProductId.");
        Description(b =>
        {
            b.Produces<StockLevelResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
        });
    }

    public override async Task HandleAsync(GetStockLevelRequest request, CancellationToken ct)
    {
        var query = new GetStockLevelByProductIdQuery { ProductId = request.ProductId };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
