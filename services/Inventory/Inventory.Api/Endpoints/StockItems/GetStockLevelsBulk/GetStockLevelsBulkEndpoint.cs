using System.Net;
using FastEndpoints;
using Inventory.Application.StockItems.GetStockLevelsBulk;
using Platform.Api.Extensions;

namespace Inventory.Api.Endpoints.StockItems.GetStockLevelsBulk;

/// <summary>
/// Partial-tolerant batch stock-availability read backing the BFF basket / home-page
/// overlays (ADR-0034). <c>AllowAnonymous</c> per ADR-0034 + use-cases.md § 4.4.2 — the
/// availability overlay is public shopper-facing data. Served through the Inventory-owned
/// read-through cache; the reservation decision path never touches that cache, so the
/// endpoint cannot influence oversell safety.
/// </summary>
internal sealed class GetStockLevelsBulkEndpoint
    : Endpoint<GetStockLevelsBulkRequest, GetStockLevelsBulkResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetStockLevelsBulkQuery, GetStockLevelsBulkResponse> _handler;

    public GetStockLevelsBulkEndpoint(
        Platform.CQRS.IQueryHandler<GetStockLevelsBulkQuery, GetStockLevelsBulkResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("stock-items/bulk");
        Version(1);
        Group<InventoryGroup>();
        AllowAnonymous();
        Summary(s => s.Summary = "Returns current stock-level snapshots for up to 200 ProductIds; unknown ids are listed in missingProductIds.");
        Description(b =>
        {
            b.Produces<GetStockLevelsBulkResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(GetStockLevelsBulkRequest request, CancellationToken ct)
    {
        var query = new GetStockLevelsBulkQuery { ProductIds = request.ProductIds };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
