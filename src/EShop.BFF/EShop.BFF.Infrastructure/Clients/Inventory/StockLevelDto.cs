namespace EShop.BFF.Infrastructure.Clients.Inventory;

/// <summary>
/// BFF-internal re-declaration of Inventory's stock-availability read model (anti-corruption:
/// upstream Inventory types never cross the BFF boundary, bff.md § 1 + § 4.4). Mirrors Inventory's
/// <c>GET /api/v1/inventory/stock-items/{productId}</c> response. <c>Available</c> is the figure
/// the product page surfaces (<c>InStock = Available &gt; 0</c>).
/// </summary>
internal sealed record StockLevelDto(
    Guid ProductId,
    int OnHand,
    int Reserved,
    int Available,
    DateTimeOffset LastUpdatedUtc,
    int LastVersion);
