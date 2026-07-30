namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// Amount + currency as Catalog emits it, on every route that returns money. The one Catalog record the
/// BFF binds from more than one route: how money is represented is an API-wide decision, so a change to it
/// is necessarily the same change at every binding site — the inbound mirror of ADR-0037's
/// <c>MoneyDto</c> ruling, and the reason this type is named in the arch test's allow-list while the
/// per-route product records are not.
/// </summary>
internal sealed record CatalogMoneyDto
{
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}
