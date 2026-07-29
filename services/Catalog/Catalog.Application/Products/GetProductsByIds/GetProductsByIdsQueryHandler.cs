using Catalog.Application.Common.Contracts;
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

        var products = rows.Select(ToProductDetail).ToList();
        var foundIds = products.Select(p => p.ProductId).ToHashSet();
        var missing = requestedIds.Where(id => !foundIds.Contains(id)).ToList();

        return Result.Ok(new GetProductsByIdsResponse
        {
            Products = products,
            MissingProductIds = missing,
        });
    }

    /// <summary>
    /// Lives here rather than on <see cref="ProductDetailRow"/> because that row is shared with
    /// <c>GetProductById</c>, whose wire type is a separate one (ADR-0037) — one method cannot
    /// return both.
    /// </summary>
    private static ProductDetailResponse ToProductDetail(ProductDetailRow row) =>
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
