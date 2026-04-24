namespace Inventory.Domain.StockItems.ValueObjects;

/// <summary>
/// Read-side projection DTO for queries that do not need the full reservation list.
/// Also the shape returned by <c>GetStockLevelQuery</c> in later milestones.
/// </summary>
public sealed record StockItemSnapshot(
    Guid ProductId,
    int OnHand,
    int Reserved,
    int Available,
    int Version);
