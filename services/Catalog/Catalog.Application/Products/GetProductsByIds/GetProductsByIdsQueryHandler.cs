using Catalog.Application.Common.Data;
using Catalog.Application.Common.ReadModels;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Catalog.Application.Products.GetProductsByIds;

public sealed class GetProductsByIdsQueryHandler
    : IQueryHandler<GetProductsByIdsQuery, GetProductsByIdsResponse>
{
    private readonly ICatalogDbContext _db;

    public GetProductsByIdsQueryHandler(ICatalogDbContext db)
    {
        _db = db;
    }

    public async Task<Result<GetProductsByIdsResponse>> HandleAsync(
        GetProductsByIdsQuery query,
        CancellationToken ct)
    {
        var requestedIds = query.Ids.ToHashSet();

        var rows = await _db.ProductSearchView
            .AsNoTracking()
            .Where(r => requestedIds.Contains(r.ProductId))
            .TagWith(nameof(GetProductsByIdsQueryHandler))
            .Select(ProductDetailRow.Projection)
            .ToListAsync(ct);

        var products = rows.Select(r => r.ToResponse()).ToList();
        var foundIds = products.Select(p => p.ProductId).ToHashSet();
        var missing = requestedIds.Where(id => !foundIds.Contains(id)).ToList();

        return Result.Ok(new GetProductsByIdsResponse
        {
            Products = products,
            MissingProductIds = missing,
        });
    }
}
