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

    public GetStockLevelByProductIdQueryHandler(IInventoryDbContext db)
    {
        _db = db;
    }

    public async Task<Result<StockLevelResponse>> HandleAsync(
        GetStockLevelByProductIdQuery query,
        CancellationToken ct)
    {
        var response = await _db.CurrentStockLevels
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
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return response is null
            ? Result.Fail<StockLevelResponse>(InventoryErrors.StockItemNotFound(query.ProductId))
            : Result.Ok(response);
    }
}
