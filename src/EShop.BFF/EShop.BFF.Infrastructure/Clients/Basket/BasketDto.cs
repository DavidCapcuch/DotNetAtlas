namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>
/// BFF-internal re-declaration of Basket's <c>GET /api/v1/basket</c> response (anti-corruption: upstream
/// Basket types never cross the BFF boundary, bff.md § 1 + § 4.2). The enriched basket page (bff.md § 3.2)
/// overlays <em>current</em> Catalog price + Inventory availability onto these <em>snapshot</em> lines.
/// </summary>
internal sealed record BasketDto
{
    public required Guid UserId { get; init; }

    public required int Version { get; init; }

    public required IReadOnlyList<BasketItemDto> Items { get; init; }

    /// <summary><c>null</c> upstream for an empty basket (Basket returns no total without items).</summary>
    public BasketMoneyDto? Total { get; init; }
}

internal sealed record BasketItemDto
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required BasketMoneyDto SnapshotPrice { get; init; }

    public required int Quantity { get; init; }
}

internal sealed record BasketMoneyDto
{
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}
