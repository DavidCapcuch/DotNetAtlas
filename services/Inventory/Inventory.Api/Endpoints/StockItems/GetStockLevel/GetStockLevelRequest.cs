using FastEndpoints;

namespace Inventory.Api.Endpoints.StockItems.GetStockLevel;

internal sealed class GetStockLevelRequest
{
    [BindFrom("productId")]
    public required Guid ProductId { get; init; }
}
