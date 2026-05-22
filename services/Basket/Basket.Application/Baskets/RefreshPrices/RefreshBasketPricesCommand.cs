using Platform.CQRS;

namespace Basket.Application.Baskets.RefreshPrices;

/// <summary>
/// Re-fetches product snapshots from Catalog for every distinct line in the basket
/// owned by <paramref name="UserId"/> and replaces them, preserving quantities.
/// Missing basket ⇒ no-op <see cref="FluentResults.Result.Ok"/>. A full Catalog
/// failure ⇒ <c>BasketAclErrors.CatalogUnavailable</c>; partial Catalog responses are
/// tolerated (missing products leave their snapshots untouched).
/// </summary>
public sealed record RefreshBasketPricesCommand(Guid UserId) : ICommand;
