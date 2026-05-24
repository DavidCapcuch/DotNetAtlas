using Inventory.Application.StockItems.Common;
using Platform.CQRS;

namespace Inventory.Application.StockItems.GetStockLevelByProductId;

/// <summary>
/// Public query — read the hot-path <c>current_stock_levels</c> projection for
/// a single <c>ProductId</c>. Drives <c>GET /api/v1/inventory/stock-items/{productId}</c>.
/// </summary>
public sealed record GetStockLevelByProductIdQuery : IQuery<StockLevelResponse>
{
    public required Guid ProductId { get; init; }
}
