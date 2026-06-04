using FastEndpoints;

namespace Inventory.Api.Endpoints.StockItems.Receive;

/// <summary>
/// Body for <c>POST /api/v1/inventory/stock-items/{productId}/receive</c>.
/// <c>ProductId</c> is bound from the route token; the rest from the body.
/// </summary>
internal sealed class ReceiveStockRequest
{
    [BindFrom("productId")]
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }

    public required string Source { get; init; }

    public Guid? ReceivedByUserId { get; init; }
}
