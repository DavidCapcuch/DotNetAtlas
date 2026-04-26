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
        var row = await _db.CurrentStockLevels
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProductId == query.ProductId, ct)
            .ConfigureAwait(false);

        return row is null
            ? Result.Fail<StockLevelResponse>(InventoryErrors.StockItemNotFound(query.ProductId))
            : Result.Ok(row.ToStockLevelResponse());
    }
}
