using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults;

namespace Basket.Application.Abstractions;

/// <summary>
/// Anti-Corruption Layer port — the only way Basket reads product data from Catalog.
/// </summary>
/// <remarks>
/// <para>
/// Concrete implementation: <c>ProductCatalogHttpAdapter</c> in <c>Basket.Infrastructure</c>
/// (milestone M5). The adapter converts Catalog's <c>CatalogProductResponse</c> DTO into the
/// Basket-owned <see cref="ProductSnapshot"/> VO (see <c>basket.md § 9</c>). Nothing below
/// this port references Catalog types — keeping the coupling pointed at a single seam.
/// </para>
/// <para>
/// <b>Error contract</b> (basket.md § 9.1):
/// <list type="bullet">
///   <item><see cref="BasketErrors.CatalogUnavailable"/> on HTTP 5xx, network error, timeout, cancellation.</item>
///   <item><see cref="BasketErrors.ProductNotFound"/> on HTTP 404 (single-id call only).</item>
/// </list>
/// </para>
/// </remarks>
public interface IProductCatalogQueryPort
{
    /// <summary>
    /// Fetches a single product snapshot. Returns <see cref="BasketErrors.ProductNotFound"/>
    /// when the product does not exist, and <see cref="BasketErrors.CatalogUnavailable"/>
    /// on transport failures.
    /// </summary>
    Task<Result<ProductSnapshot>> GetProductSnapshotAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Fetches many product snapshots in a single round-trip, paired with the
    /// <see cref="Guid"/> each snapshot belongs to so callers can match them back
    /// to basket items (<see cref="ProductSnapshot"/> does not carry a product id).
    /// Partial-tolerant — ids that are not present in the Catalog response are silently
    /// dropped; the caller decides what to do with the missing ones (typically
    /// <c>RefreshPrices</c> leaves them untouched). Returns
    /// <see cref="BasketErrors.CatalogUnavailable"/> only on a full transport failure.
    /// </summary>
    Task<Result<IReadOnlyList<(Guid ProductId, ProductSnapshot Snapshot)>>> GetManyAsync(
        IEnumerable<Guid> productIds,
        CancellationToken ct);
}
