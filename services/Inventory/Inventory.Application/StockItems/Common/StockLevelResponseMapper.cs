using Inventory.Application.Common.ReadModels;

namespace Inventory.Application.StockItems.Common;

/// <summary>
/// Read-model row → public response DTO. Hand-written extension method matches
/// the existing internal-event mapper style used elsewhere in this layer
/// (<c>StockLevelChangedMapper</c>, <c>StockReservedMapper</c>).
/// </summary>
internal static class StockLevelResponseMapper
{
    public static StockLevelResponse ToStockLevelResponse(this CurrentStockLevelRow row) =>
        new()
        {
            ProductId = row.ProductId,
            OnHand = row.OnHand,
            Reserved = row.Reserved,
            Available = row.Available,
            LastUpdatedUtc = row.LastUpdatedUtc,
            LastVersion = row.LastVersion,
        };
}
