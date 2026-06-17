namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>
/// BFF-internal re-declaration of Basket's <c>GET /api/v1/basket</c> response (anti-corruption: upstream
/// Basket types never cross the BFF boundary, bff.md § 1 + § 4.2). The enriched basket page (bff.md § 3.2)
/// overlays <em>current</em> Catalog price + Inventory availability onto these <em>snapshot</em> lines.
/// <see cref="Total"/> is <c>null</c> for an empty basket (Basket returns no total without items).
/// </summary>
internal sealed record BasketDto(
    Guid UserId,
    int Version,
    IReadOnlyList<BasketItemDto> Items,
    BasketMoneyDto? Total,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastModifiedAtUtc);

internal sealed record BasketItemDto(
    Guid ProductId,
    string Sku,
    string Name,
    BasketMoneyDto SnapshotPrice,
    int Quantity,
    DateTimeOffset CapturedAtUtc,
    BasketMoneyDto LineTotal);

internal sealed record BasketMoneyDto(decimal Amount, string Currency);
