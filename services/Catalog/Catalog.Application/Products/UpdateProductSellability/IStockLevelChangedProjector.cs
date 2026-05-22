namespace Catalog.Application.Products.UpdateProductSellability;

/// <summary>
/// Application-layer port consumed by Infrastructure Kafka adapters that handle Inventory's
/// <c>StockLevelChanged</c> events. The implementation
/// (<see cref="StockLevelChangedProjectionHandler"/>) lives in Catalog.Application so
/// architecture-tests.md § 2.1 ("projection writes only in *ProjectionHandler") holds across
/// the inbox-driven cross-BC path as well (CAT-ARCH-C02 / #174). The interface is named with a
/// <c>Projector</c> suffix rather than <c>ProjectionHandler</c> to keep the architecture-tests
/// rule "*ProjectionHandler types must be sealed" applicable only to concrete classes.
/// </summary>
public interface IStockLevelChangedProjector
{
    /// <summary>
    /// Recomputes <c>IsSellable</c> on the <c>product_search_view</c> row for
    /// <paramref name="productId"/> using <paramref name="newAvailable"/> and the row's current
    /// status. No-op when the row is missing (logged) or when <c>IsSellable</c> would not change.
    /// </summary>
    Task HandleAsync(Guid productId, int newAvailable, CancellationToken ct);
}
