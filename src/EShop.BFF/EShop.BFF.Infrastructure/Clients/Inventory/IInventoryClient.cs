using FluentResults;

namespace EShop.BFF.Infrastructure.Clients.Inventory;

/// <summary>
/// Typed client for Inventory's stock-availability read (bff.md § 4.4). A failed result (timeout /
/// 5xx / 404 → "unknown availability", bff.md § 3.1) never gates the page — the composer renders the
/// product with null availability and flags partial/stale data.
/// </summary>
internal interface IInventoryClient
{
    Task<Result<StockLevelDto>> GetStockLevelAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Reads availability for many products in one call (bff.md § 4.4) — the home page's stock overlay. A
    /// failed result (timeout / 5xx) never gates the page; the overlay is dropped (null availability, no
    /// highlights) and partial/stale data is flagged. Products with no initialized stock item come back in
    /// <see cref="StockLevelsBulkDto.MissingProductIds"/> rather than failing the call.
    /// </summary>
    Task<Result<StockLevelsBulkDto>> GetStockLevelsBulkAsync(
        IReadOnlyList<Guid> productIds, CancellationToken ct);
}
