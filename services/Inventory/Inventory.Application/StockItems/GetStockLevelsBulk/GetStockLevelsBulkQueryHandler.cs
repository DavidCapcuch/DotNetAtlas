using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.Common;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Inventory.Application.StockItems.GetStockLevelsBulk;

/// <summary>
/// Serves the batch display read through the Inventory-owned read-through cache
/// (<see cref="IStockLevelCache"/>): per-id cache hits plus a single
/// <c>WHERE ProductId = ANY(@missing)</c> projection read for the misses (ADR-0034).
/// Partial-tolerant — ids with no projection row are returned in
/// <see cref="GetStockLevelsBulkResponse.MissingProductIds"/> rather than failing the call.
/// </summary>
internal sealed class GetStockLevelsBulkQueryHandler
    : IQueryHandler<GetStockLevelsBulkQuery, GetStockLevelsBulkResponse>
{
    private readonly IInventoryDbContext _db;
    private readonly IStockLevelCache _cache;

    public GetStockLevelsBulkQueryHandler(IInventoryDbContext db, IStockLevelCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Result<GetStockLevelsBulkResponse>> HandleAsync(
        GetStockLevelsBulkQuery query,
        CancellationToken ct)
    {
        // Dedupe so a repeated id is read / reported once.
        var requestedIds = query.ProductIds.Distinct().ToList();

        var found = await _cache.GetManyAsync(
            requestedIds,
            async (missing, token) =>
            {
                var missingIds = missing as Guid[] ?? missing.ToArray();

                return await _db.CurrentStockLevels
                    .AsNoTracking()
                    .Where(r => missingIds.Contains(r.ProductId))
                    .TagWith(nameof(GetStockLevelsBulkQueryHandler))
                    .Select(r => new StockLevelResponse
                    {
                        ProductId = r.ProductId,
                        OnHand = r.OnHand,
                        Reserved = r.Reserved,
                        Available = r.Available,
                        LastUpdatedUtc = r.LastUpdatedUtc,
                        LastVersion = r.LastVersion,
                    })
                    .ToListAsync(token)
                    .ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);

        var foundIds = found.Select(item => item.ProductId).ToHashSet();
        var items = found.Select(item => item.ToBulkItem()).ToList();
        var missingProductIds = requestedIds.Where(id => !foundIds.Contains(id)).ToList();

        return Result.Ok(new GetStockLevelsBulkResponse
        {
            Items = items,
            MissingProductIds = missingProductIds,
        });
    }
}
