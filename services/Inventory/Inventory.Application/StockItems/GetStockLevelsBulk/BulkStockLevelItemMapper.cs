using Inventory.Application.StockItems.Common;

namespace Inventory.Application.StockItems.GetStockLevelsBulk;

/// <summary>
/// Cached single-read DTO → batch item. Hand-written extension matching the existing
/// mapper style in this layer (<c>StockLevelResponseMapper</c>); drops <c>LastVersion</c>
/// which the batch contract omits.
/// </summary>
internal static class BulkStockLevelItemMapper
{
    public static BulkStockLevelItem ToBulkItem(this StockLevelResponse response) =>
        new()
        {
            ProductId = response.ProductId,
            OnHand = response.OnHand,
            Reserved = response.Reserved,
            Available = response.Available,
            LastUpdatedUtc = response.LastUpdatedUtc,
        };
}
