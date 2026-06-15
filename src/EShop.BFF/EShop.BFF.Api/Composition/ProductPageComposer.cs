using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;

namespace EShop.BFF.Api.Composition;

/// <summary>
/// Pure composition of the product page (bff.md § 3.1): merges a (successful) Catalog product with
/// Inventory availability into a <see cref="ProductPageResponse"/>. Catalog gating (404 → 404,
/// transport failure → fail-safe / 503) lives in the endpoint; this type only runs once Catalog has
/// succeeded, so it is unit-testable in isolation with no I/O.
/// </summary>
internal static class ProductPageComposer
{
    /// <summary>
    /// Composes the page. <paramref name="stockOrNull"/> is <c>null</c> when Inventory was
    /// unavailable (timeout / 5xx / 404 → unknown availability, bff.md § 3.1): availability fields
    /// become <c>null</c> and <see cref="ProductPageResponse.HasStaleData"/> is set.
    /// </summary>
    public static ProductPageResponse Compose(
        CatalogProductDto product,
        StockLevelDto? stockOrNull,
        DateTimeOffset generatedAtUtc)
    {
        var productDetail = MapProduct(product);

        if (stockOrNull is not null)
        {
            return new ProductPageResponse
            {
                Product = productDetail,
                InStock = stockOrNull.Available > 0,
                AvailableQty = stockOrNull.Available,
                HasStaleData = false,
                GeneratedAtUtc = generatedAtUtc,
            };
        }

        return new ProductPageResponse
        {
            Product = productDetail,
            InStock = null,
            AvailableQty = null,
            HasStaleData = true,
            GeneratedAtUtc = generatedAtUtc,
        };
    }

    private static ProductDetailDto MapProduct(CatalogProductDto product) =>
        new()
        {
            ProductId = product.ProductId,
            Sku = product.Sku,
            Name = product.Name,
            Description = product.Description,
            BrandName = product.BrandName,
            CategoryBreadcrumb = product.CategoryBreadcrumb,
            CategoryPath = product.CategoryPath,
            Price = new MoneyDto(product.Price.Amount, product.Price.Currency),
            Dimensions = product.Dimensions is null
                ? null
                : new DimensionsDto(
                    product.Dimensions.Length,
                    product.Dimensions.Width,
                    product.Dimensions.Height,
                    product.Dimensions.Unit),
            Images = product.Images
                .Select(image => new ProductImageDto(image.Url, image.AltText, image.DisplayOrder))
                .ToList(),
            Status = product.Status,
        };
}
