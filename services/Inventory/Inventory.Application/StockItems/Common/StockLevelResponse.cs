namespace Inventory.Application.StockItems.Common;

/// <summary>
/// Public read-side projection of <c>inventory.current_stock_levels</c>.
/// Returned by <see cref="GetStockLevelByProductId.GetStockLevelByProductIdQuery"/>
/// and by the admin <c>ReceiveStock</c> / <c>AdjustStock</c> command handlers
/// (post-mutation snapshot).
/// </summary>
/// <remarks>
/// Wire-shape exposed at the HTTP boundary; mirrors
/// <see cref="Inventory.Application.Common.ReadModels.CurrentStockLevelRow"/>
/// minus the projection-internal <c>PreviousAvailable</c> field which has no
/// meaning to callers. <c>LastVersion</c> and <c>LastUpdatedUtc</c> are
/// surfaced because admin tooling uses them for forensic timelines.
/// </remarks>
public sealed class StockLevelResponse
{
    public required Guid ProductId { get; init; }

    public required int OnHand { get; init; }

    public required int Reserved { get; init; }

    public required int Available { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }

    public required int LastVersion { get; init; }
}
