using System.Linq.Expressions;
using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.SearchProducts;

namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// SQL-side projection of <see cref="ProductSearchViewRow"/> carrying only the columns a search
/// result item needs — notably NOT the unbounded <c>Description</c> text. Shared by
/// <c>SearchProductsQueryHandler</c> and <c>GetProductsByCategoryQueryHandler</c> (ADR-0021).
/// <c>ImagesJson</c> is carried through and deserialized in <see cref="ToResultItem"/> to derive
/// the primary image URL after the SQL projection.
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

    public SearchProductsResultItem ToResultItem()
    {
        var images = ProductSearchViewMapper.DeserializeImages(ImagesJson);
        var primaryUrl = images.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.Url;

        return new SearchProductsResultItem
        {
            ProductId = ProductId,
            Sku = Sku,
            Name = Name,
            CategoryBreadcrumb = CategoryBreadcrumb,
            BrandName = BrandName,
            Price = new MoneyDto { Amount = PriceAmount, Currency = PriceCurrency },
            Status = Status,
            PrimaryImageUrl = primaryUrl,
        };
    }
}
