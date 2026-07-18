using Catalog.Application.Common.Contracts;
using Catalog.Application.Common.Data;
using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Products.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Catalog.Application.Products.GetProductById;

public sealed class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDetailResponse>
{
    private readonly ICatalogDbContext _db;

    public GetProductByIdQueryHandler(ICatalogDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ProductDetailResponse>> HandleAsync(GetProductByIdQuery query, CancellationToken ct)
    {
        var row = await _db.ProductSearchView
            .AsNoTracking()
            .Where(r => r.ProductId == query.ProductId)
            .TagWith(nameof(GetProductByIdQueryHandler))
            .Select(ProductDetailRow.Projection)
            .FirstOrDefaultAsync(ct);

        return row is null
            ? Result.Fail<ProductDetailResponse>(ProductErrors.NotFound(query.ProductId))
            : Result.Ok(row.ToResponse());
    }
}
