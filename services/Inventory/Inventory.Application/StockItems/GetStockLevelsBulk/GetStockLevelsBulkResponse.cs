namespace Inventory.Application.StockItems.GetStockLevelsBulk;

/// <summary>
/// Partial-tolerant batch response: <see cref="Items"/> carries one entry per known
/// product; <see cref="MissingProductIds"/> lists the requested ids that have no
/// projection row (uninitialized or unknown product). The two collections together
/// account for every distinct requested id (ADR-0034).
/// </summary>
public sealed class GetStockLevelsBulkResponse
{
    public required IReadOnlyList<BulkStockLevelItem> Items { get; init; }

    public required IReadOnlyList<Guid> MissingProductIds { get; init; }
}
