namespace EShop.BFF.Infrastructure.Clients.Inventory;

/// <summary>
/// BFF-internal projection of Inventory's <c>POST /api/v1/inventory/stock-items/bulk</c> response
/// (anti-corruption, bff.md § 4.4). Carries only the per-product availability the pages render.
/// Inventory also returns the ids with no initialized stock item; the BFF does not bind them — a
/// product absent from <see cref="Items"/> already renders as "availability unknown", so binding the
/// list would add a member no page reads and one more thing Inventory could not drop (bff.md § 4.1).
/// </summary>
internal sealed record StockLevelsBulkDto
{
    public required IReadOnlyList<BulkStockLevelDto> Items { get; init; }
}

internal sealed record BulkStockLevelDto
{
    public required Guid ProductId { get; init; }

    public required int Available { get; init; }
}
