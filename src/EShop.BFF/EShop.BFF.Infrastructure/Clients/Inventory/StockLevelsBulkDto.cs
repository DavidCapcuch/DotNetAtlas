namespace EShop.BFF.Infrastructure.Clients.Inventory;

/// <summary>
/// BFF-internal re-declaration of Inventory's bulk stock-availability read (anti-corruption,
/// bff.md § 4.4). Mirrors Inventory's <c>POST /api/v1/inventory/stock-items/bulk</c> response:
/// availability for the requested products plus the ids with no initialized stock item
/// (<see cref="MissingProductIds"/>) — those render as "availability unknown".
/// </summary>
internal sealed record StockLevelsBulkDto(
    IReadOnlyList<BulkStockLevelDto> Items,
    IReadOnlyList<Guid> MissingProductIds);

internal sealed record BulkStockLevelDto(
    Guid ProductId,
    int OnHand,
    int Reserved,
    int Available,
    DateTimeOffset LastUpdatedUtc);
