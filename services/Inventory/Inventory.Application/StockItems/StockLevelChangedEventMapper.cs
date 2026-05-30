using Inventory.Application.Common.ReadModels;
using Inventory.Stock;

namespace Inventory.Application.StockItems;

/// <summary>
/// Maps the post-event projection snapshot to the external Avro
/// <see cref="StockLevelChangedEvent"/>. Called only when the threshold-crossing
/// predicate fires (<c>PreviousAvailable == 0 XOR NewAvailable == 0</c>) in
/// <c>CurrentStockLevelsProjectionDomainEventHandler</c>.
/// </summary>
internal static class StockLevelChangedEventMapper
{
    public static StockLevelChangedEvent ToStockLevelChangedEvent(
        this CurrentStockLevelRow row,
        DateTimeOffset changedAtUtc) =>
        new()
        {
            ProductId = row.ProductId,
            NewOnHand = row.OnHand,
            NewReserved = row.Reserved,
            NewAvailable = row.Available,
            ChangedAtUtc = changedAtUtc.UtcDateTime,
        };
}
