using System.Net;
using FastEndpoints;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.GetStockLevelByProductId;
using Platform.Api.Extensions;

namespace Inventory.Api.Endpoints.StockItems.GetStockLevel;

/// <summary>
/// Single-product stock-availability read backing the public product-page overlay
/// (use-cases.md § 4.4.1). <c>AllowAnonymous</c> — the same posture as its bulk sibling
/// (<c>POST /stock-items/bulk</c>, ADR-0034): availability is public shopper-facing data.
/// Served through the Inventory-owned read-through cache; the reservation decision path never
/// touches that cache, so the endpoint cannot influence oversell safety.
/// </summary>
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
        AllowAnonymous();
        Summary(s => s.Summary = "Returns the current stock-level snapshot for a ProductId.");
        Description(b =>
        {
            b.Produces<StockLevelResponse>((int)HttpStatusCode.OK);
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
