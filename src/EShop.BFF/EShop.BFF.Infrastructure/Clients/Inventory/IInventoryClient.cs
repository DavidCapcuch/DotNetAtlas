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
}
