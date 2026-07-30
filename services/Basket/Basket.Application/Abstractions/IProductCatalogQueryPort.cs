using Basket.Application.Baskets.Common.Errors;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults;

namespace Basket.Application.Abstractions;

/// <summary>
/// Anti-Corruption Layer port — the only way Basket reads product data from Catalog.
/// </summary>
/// <remarks>
/// <para>
/// Concrete implementation: <c>ProductCatalogHttpAdapter</c> in <c>Basket.Infrastructure</c>.
/// The adapter converts Catalog's per-route response records into the Basket-owned
/// <see cref="ProductSnapshot"/> VO (see <c>basket.md § 9</c>). Nothing below this port
/// references Catalog types — keeping the coupling pointed at a single seam.
/// </para>
/// <para>
/// <b>Error contract</b> (basket.md § 9.1):
/// <list type="bullet">
///   <item><see cref="BasketAclErrors.CatalogUnavailable"/> on HTTP 5xx, network error,
///   cancellation-by-timeout, or a 200 whose body the ACL cannot bind — all of which mean "no
///   usable product". Caller-initiated cancellation is rethrown, not mapped.</item>
///   <item><see cref="BasketAclErrors.ProductNotFound"/> on HTTP 404 (single-id call only).</item>
///   <item>A <c>DataIntegrityException</c> is <b>thrown, not returned</b>, when a body that binds
///   violates a <see cref="ProductSnapshot"/> field invariant. Bug-class: retrying cannot fix
///   the upstream's data, so it fails closed instead of joining the retryable bucket above
///   (basket.md &#xa7; 3.2).</item>
/// </list>
/// </para>
/// </remarks>
public interface IProductCatalogQueryPort
{
    /// <summary>
    /// Fetches a single product snapshot. Returns <see cref="BasketAclErrors.ProductNotFound"/>
    /// when the product does not exist, and <see cref="BasketAclErrors.CatalogUnavailable"/>
    /// on a transport failure or a response body the ACL cannot bind.
    /// </summary>
    Task<Result<ProductSnapshot>> GetProductSnapshotAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Fetches many product snapshots in a single round-trip, paired with the
    /// <see cref="Guid"/> each snapshot belongs to so callers can match them back
    /// to basket items (<see cref="ProductSnapshot"/> does not carry a product id).
    /// Partial-tolerant — ids that are not present in the Catalog response are silently
    /// dropped; the caller decides what to do with the missing ones (typically
    /// <c>RefreshPrices</c> leaves them untouched). Returns
    /// <see cref="BasketAclErrors.CatalogUnavailable"/> on a transport failure or a response body
    /// the ACL cannot bind — partial-tolerance covers unmatched ids, not an unusable payload.
    /// </summary>
    Task<Result<IReadOnlyList<(Guid ProductId, ProductSnapshot Snapshot)>>> GetManyAsync(
        IEnumerable<Guid> productIds,
        CancellationToken ct);
}
