using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;

namespace EShop.BFF.Api.Composition;

/// <summary>
/// Pure composition of the home page (bff.md § 3.4): merges a (successful) Catalog product search with
/// the full category tree and the Inventory bulk availability overlay into a <see cref="HomePageResponse"/>.
/// Catalog-search gating (transport failure → fail-safe / 503) lives in the endpoint; this type only runs
/// once the search has succeeded, so it is unit-testable in isolation with no I/O.
/// </summary>
internal static class HomePageComposer
{
    // "Running low" threshold for the stock highlights (bff.md § 3.4): 0 < AvailableQty <= 10.
    private const int RunningLowThreshold = 10;

    /// <summary>
    /// Composes the page. <paramref name="categoryTreeOrNull"/> is <c>null</c> when Catalog's category-tree
    /// read was unavailable (the page keeps its featured products but drops the tree). <paramref name="stockOrNull"/>
    /// is <c>null</c> when the Inventory bulk overlay was unavailable (every product's availability becomes
    /// <c>null</c> and no highlights are derived). Either case sets <see cref="HomePageResponse.HasStaleData"/>.
    /// </summary>
    public static HomePageResponse Compose(
        IReadOnlyList<CatalogProductSummaryDto> featured,
        CategoryTreeDto? categoryTreeOrNull,
        StockLevelsBulkDto? stockOrNull,
        DateTimeOffset generatedAtUtc)
    {
        var availabilityByProduct = stockOrNull?.Items.ToDictionary(item => item.ProductId);

        var featuredProducts = featured
            .Select(product => MapFeatured(product, availabilityByProduct))
            .ToList();

        return new HomePageResponse
        {
            FeaturedProducts = featuredProducts,
            CategoryTree = categoryTreeOrNull is null ? null : MapTree(categoryTreeOrNull),
            StockHighlights = stockOrNull is null ? null : DeriveHighlights(featuredProducts),
            HasStaleData = categoryTreeOrNull is null || stockOrNull is null,
            GeneratedAtUtc = generatedAtUtc,
        };
    }

    private static FeaturedProductDto MapFeatured(
        CatalogProductSummaryDto product,
        Dictionary<Guid, BulkStockLevelDto>? availabilityByProduct)
    {
        // No overlay (Inventory down) or this product has no initialized stock item → availability unknown.
        var available = availabilityByProduct is not null
            && availabilityByProduct.TryGetValue(product.ProductId, out var stock)
            ? stock.Available
            : (int?)null;

        return new FeaturedProductDto
        {
            ProductId = product.ProductId,
            Sku = product.Sku,
            Name = product.Name,
            BrandName = product.BrandName,
            CategoryBreadcrumb = product.CategoryBreadcrumb,
            Price = new MoneyDto(product.Price.Amount, product.Price.Currency),
            PrimaryImageUrl = product.PrimaryImageUrl,
            InStock = available is null ? null : available > 0,
            AvailableQty = available,
        };
    }

    private static HomeCategoryTreeDto MapTree(CategoryTreeDto tree) =>
        new()
        {
            Nodes = tree.Nodes
                .Select(node => new HomeCategoryNodeDto
                {
                    CategoryId = node.CategoryId,
                    Name = node.Name,
                    Path = node.Path,
                    ParentCategoryId = node.ParentCategoryId,
                    Depth = node.Depth,
                    ProductCount = node.ProductCount,
                })
                .ToList(),
        };

    private static List<StockHighlightDto> DeriveHighlights(IReadOnlyList<FeaturedProductDto> featuredProducts) =>
        featuredProducts
            .Where(product => product.AvailableQty is > 0 and <= RunningLowThreshold)
            .Select(product => new StockHighlightDto(product.ProductId, product.Name, product.AvailableQty!.Value))
            .ToList();
}
