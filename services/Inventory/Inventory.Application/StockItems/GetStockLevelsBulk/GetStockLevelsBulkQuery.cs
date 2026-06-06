using Platform.CQRS;

namespace Inventory.Application.StockItems.GetStockLevelsBulk;

/// <summary>
/// Public, partial-tolerant batch query — read the hot-path <c>current_stock_levels</c>
/// projection for up to 200 <c>ProductId</c>s in one round trip. Backs the BFF basket /
/// home-page availability overlays (ADR-0034); drives
/// <c>POST /api/v1/inventory/stock-items/bulk</c>.
/// </summary>
public sealed record GetStockLevelsBulkQuery : IQuery<GetStockLevelsBulkResponse>
{
    public required IReadOnlyList<Guid> ProductIds { get; init; }
}
