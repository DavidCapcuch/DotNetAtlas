namespace Inventory.Application.StockItems.GetStockLevelsBulk;

/// <summary>
/// One product's stock level in a <see cref="GetStockLevelsBulkResponse"/>. Mirrors
/// the single-read shape minus <c>LastVersion</c> — the batch contract (use-cases.md
/// § 4.4.2) omits the forensic stream version that only admin tooling consumes.
/// </summary>
public sealed class BulkStockLevelItem
{
    public required Guid ProductId { get; init; }

    public required int OnHand { get; init; }

    public required int Reserved { get; init; }

    public required int Available { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }
}
