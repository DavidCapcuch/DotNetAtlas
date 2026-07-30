namespace EShop.BFF.Infrastructure.Clients.Inventory;

/// <summary>
/// BFF-internal projection of Inventory's <c>GET /api/v1/inventory/stock-items/{productId}</c> response
/// (anti-corruption: upstream Inventory types never cross the BFF boundary, bff.md § 1 + § 4.4). Declares
/// only what the product page renders — <c>InStock = Available &gt; 0</c> plus the figure itself. The
/// response's other members are deliberately unbound: an ACL record declares what the BFF requires, and
/// every member it declares is one Inventory cannot drop without degrading the page (bff.md § 4.1).
/// </summary>
internal sealed record StockLevelDto
{
    public required int Available { get; init; }
}
