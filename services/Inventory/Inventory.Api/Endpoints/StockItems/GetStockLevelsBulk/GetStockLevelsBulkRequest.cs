namespace Inventory.Api.Endpoints.StockItems.GetStockLevelsBulk;

/// <summary>
/// Request body for <c>POST /api/v1/inventory/stock-items/bulk</c>. POST is used because
/// the id list may exceed URL length for basket-sized collections; the body is read-only
/// despite the verb (use-cases.md § 4.4.2).
/// </summary>
internal sealed class GetStockLevelsBulkRequest
{
    public required IReadOnlyList<Guid> ProductIds { get; init; }
}
