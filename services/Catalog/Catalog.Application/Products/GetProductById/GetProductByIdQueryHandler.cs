using Catalog.Application.Common.Data;
using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Products.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Catalog.Application.Products.GetProductById;

public sealed class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, GetProductByIdResponse>
{
    private readonly ICatalogDbContext _db;

    public GetProductByIdQueryHandler(ICatalogDbContext db)
    {
        _db = db;
    }

    public async Task<Result<GetProductByIdResponse>> HandleAsync(GetProductByIdQuery query, CancellationToken ct)
    {
        var row = await _db.ProductSearchView
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProductId == query.ProductId, ct);

        if (row is null)
        {
            return Result.Fail<GetProductByIdResponse>(ProductErrors.NotFound(query.ProductId));
        }

        return Result.Ok(ProductSearchViewMapper.ToDetailResponse(row));
    }
}
