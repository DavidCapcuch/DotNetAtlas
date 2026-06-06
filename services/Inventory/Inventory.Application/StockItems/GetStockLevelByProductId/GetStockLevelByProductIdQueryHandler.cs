using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.Common;
using Inventory.Domain.StockItems.Errors;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Inventory.Application.StockItems.GetStockLevelByProductId;

internal sealed class GetStockLevelByProductIdQueryHandler
    : IQueryHandler<GetStockLevelByProductIdQuery, StockLevelResponse>
{
    private readonly IInventoryDbContext _db;
    private readonly IStockLevelCache _cache;

    public GetStockLevelByProductIdQueryHandler(IInventoryDbContext db, IStockLevelCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Result<StockLevelResponse>> HandleAsync(
        GetStockLevelByProductIdQuery query,
        CancellationToken ct)
    {
        // Read-through the Inventory-owned cache (ADR-0034). A miss runs the projection
        // read below; an unknown product returns null (not cached) and maps to a 404.
        var response = await _cache.GetOrSetAsync(
            query.ProductId,
            token => _db.CurrentStockLevels
                .AsNoTracking()
                .Where(r => r.ProductId == query.ProductId)
                .TagWith(nameof(GetStockLevelByProductIdQueryHandler))
                .Select(r => new StockLevelResponse
                {
                    ProductId = r.ProductId,
                    OnHand = r.OnHand,
                    Reserved = r.Reserved,
                    Available = r.Available,
                    LastUpdatedUtc = r.LastUpdatedUtc,
                    LastVersion = r.LastVersion,
                })
                .FirstOrDefaultAsync(token),
            ct).ConfigureAwait(false);

        return response is null
            ? Result.Fail<StockLevelResponse>(InventoryErrors.StockItemNotFound(query.ProductId))
            : Result.Ok(response);
    }
}
