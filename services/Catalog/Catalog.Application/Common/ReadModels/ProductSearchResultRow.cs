using System.Linq.Expressions;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// SQL-side projection of <see cref="ProductSearchViewRow"/> carrying only the columns a search
/// result item needs — notably NOT the unbounded <c>Description</c> text. Shared by
/// <c>SearchProductsQueryHandler</c> and <c>GetProductsByCategoryQueryHandler</c> (ADR-0021).
/// <c>ImagesJson</c> is carried through unparsed. Row-to-wire mapping lives in each consuming
/// handler rather than here: the two slices return different item types (ADR-0037), so one method
/// on this row could not serve both.
/// </summary>
internal sealed record ProductSearchResultRow(
    Guid ProductId,
    string Sku,
    string Name,
    string CategoryBreadcrumb,
    string BrandName,
    decimal PriceAmount,
    string PriceCurrency,
    string Status,
    string ImagesJson)
{
    public static Expression<Func<ProductSearchViewRow, ProductSearchResultRow>> Projection => row =>
        new ProductSearchResultRow(
            row.ProductId,
            row.Sku,
            row.Name,
            row.CategoryBreadcrumb,
            row.BrandName,
            row.PriceAmount,
            row.PriceCurrency,
            row.Status,
            row.ImagesJson);
}
