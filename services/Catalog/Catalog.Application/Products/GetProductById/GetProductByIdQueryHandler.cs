using Catalog.Application.Common.Contracts;
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
            .Where(r => r.ProductId == query.ProductId)
            .TagWith(nameof(GetProductByIdQueryHandler))
            .Select(ProductDetailRow.Projection)
            .FirstOrDefaultAsync(ct);

        return row is null
            ? Result.Fail<GetProductByIdResponse>(ProductErrors.NotFound(query.ProductId))
            : Result.Ok(ToResponse(row));
    }

    /// <summary>
    /// Lives here rather than on <see cref="ProductDetailRow"/> because that row is shared with
    /// <c>GetProductsByIds</c>, whose wire type is a separate one (ADR-0037) — one method cannot
    /// return both.
    /// </summary>
    private static GetProductByIdResponse ToResponse(ProductDetailRow row) =>
        new()
        {
            ProductId = row.ProductId,
            Sku = row.Sku,
            Name = row.Name,
            Description = row.Description,
            CategoryId = row.CategoryId,
            CategoryPath = row.CategoryPath,
            CategoryBreadcrumb = row.CategoryBreadcrumb,
            BrandName = row.BrandName,
            Price = new MoneyDto { Amount = row.PriceAmount, Currency = row.PriceCurrency },
            Status = row.Status,
            Dimensions = ProductSearchViewMapper.ToDimensionsDto(
                length: row.DimensionsLength,
                width: row.DimensionsWidth,
                height: row.DimensionsHeight,
                unit: row.DimensionsUnit),
            Images = ProductSearchViewMapper.ToImageDtos(row.ImagesJson),
            CreatedAtUtc = row.CreatedAtUtc,
            LastUpdatedAtUtc = row.LastUpdatedAtUtc,
        };
}
