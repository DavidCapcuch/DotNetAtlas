using System.Linq.Expressions;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// SQL-side projection of <see cref="ProductSearchViewRow"/> carrying exactly the columns the
/// product-detail response needs — everything except <c>IsSellable</c>.
/// Shared by <c>GetProductByIdQueryHandler</c> and <c>GetProductsByIdsQueryHandler</c> (ADR-0021)
/// so neither materializes the full read-model row. <c>ImagesJson</c> is carried through as the raw
/// string because the SQL projection cannot parse it; each handler deserializes it afterwards.
/// Row-to-wire mapping lives in each consuming handler rather than here: the two slices return
/// different types (ADR-0037), so one method on this row could not serve both.
/// </summary>
internal sealed record ProductDetailRow(
    Guid ProductId,
    string Sku,
    string Name,
    string Description,
    Guid CategoryId,
    string CategoryPath,
    string CategoryBreadcrumb,
    string BrandName,
    decimal PriceAmount,
    string PriceCurrency,
    string Status,
    decimal? DimensionsLength,
    decimal? DimensionsWidth,
    decimal? DimensionsHeight,
    string? DimensionsUnit,
    string ImagesJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUpdatedAtUtc)
{
    public static Expression<Func<ProductSearchViewRow, ProductDetailRow>> Projection => row =>
        new ProductDetailRow(
            row.ProductId,
            row.Sku,
            row.Name,
            row.Description,
            row.CategoryId,
            row.CategoryPath,
            row.CategoryBreadcrumb,
            row.BrandName,
            row.PriceAmount,
            row.PriceCurrency,
            row.Status,
            row.DimensionsLength,
            row.DimensionsWidth,
            row.DimensionsHeight,
            row.DimensionsUnit,
            row.ImagesJson,
            row.CreatedAtUtc,
            row.LastUpdatedAtUtc);
}
