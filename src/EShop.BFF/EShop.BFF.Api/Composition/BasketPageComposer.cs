using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Clients.Basket;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;
using Platform.SharedKernel.ValueObjects;

namespace EShop.BFF.Api.Composition;

/// <summary>
/// Pure composition of the basket page (bff.md § 3.2): overlays <em>current</em> Catalog price (drift
/// flags) and <em>current</em> Inventory availability (out-of-stock flags) onto Basket's <em>snapshot</em>
/// lines, and computes snapshot-vs-current totals. Basket gating (transport failure → fail-safe / 503;
/// 404 → empty) lives in the endpoint; this type only shapes a (successfully read) basket, so it is
/// unit-testable in isolation with no I/O.
/// </summary>
internal static class BasketPageComposer
{
    /// <summary>
    /// Composes the page. <paramref name="catalogOrNull"/> is <c>null</c> when the Catalog batch was
    /// unavailable (every <c>CurrentPrice</c> becomes <c>null</c>, line-current falls back to snapshot);
    /// <paramref name="inventoryOrNull"/> is <c>null</c> when the Inventory batch was unavailable (every
    /// <c>AvailableQty</c> becomes <c>null</c>). A batch that succeeds but omits a product yields the same
    /// per-item nulls. Any null enrichment (batch failed or partial) sets <see cref="BasketPageResponse.HasStaleData"/>.
    /// An empty basket short-circuits to empty items + zero totals, never stale (bff.md § 3.2).
    /// </summary>
    public static BasketPageResponse Compose(
        BasketDto basket,
        CatalogProductsByIdsDto? catalogOrNull,
        StockLevelsBulkDto? inventoryOrNull,
        DateTimeOffset generatedAtUtc)
    {
        if (basket.Items.Count == 0)
        {
            // No items to enrich — an empty basket is never "stale" regardless of upstream state. The
            // currency is cosmetic on a zero total; default to the realm currency when Basket carries none.
            var emptyCurrency = basket.Total?.Currency ?? CurrencyCode.Usd.Name;
            return new BasketPageResponse
            {
                UserId = basket.UserId,
                Version = basket.Version,
                Items = [],
                TotalSnapshot = new MoneyDto(0m, emptyCurrency),
                TotalCurrent = new MoneyDto(0m, emptyCurrency),
                HasPriceDrift = false,
                HasOutOfStock = false,
                HasStaleData = false,
                GeneratedAtUtc = generatedAtUtc,
            };
        }

        var productsById = catalogOrNull?.Products.ToDictionary(product => product.ProductId);
        var availabilityById = inventoryOrNull?.Items.ToDictionary(stock => stock.ProductId);

        var items = basket.Items
            .Select(item => MapItem(item, productsById, availabilityById))
            .ToList();

        // All items share one currency (Basket aggregate invariant) — take it from the first snapshot.
        var currency = basket.Items[0].SnapshotPrice.Currency;

        // Stale when any enrichment is missing: a whole batch was unavailable, or it omitted a product
        // (→ that item's CurrentPrice / AvailableQty is null). bff.md § 3.2 step 4.
        var hasStaleData =
            catalogOrNull is null
            || inventoryOrNull is null
            || items.Any(item => item.CurrentPrice is null || item.AvailableQty is null);

        return new BasketPageResponse
        {
            UserId = basket.UserId,
            Version = basket.Version,
            Items = items,
            TotalSnapshot = new MoneyDto(items.Sum(item => item.LineTotalSnapshot.Amount), currency),
            TotalCurrent = new MoneyDto(items.Sum(item => item.LineTotalCurrent.Amount), currency),
            HasPriceDrift = items.Any(item => item.PriceDrifted),
            HasOutOfStock = items.Any(item => item.OutOfStock),
            HasStaleData = hasStaleData,
            GeneratedAtUtc = generatedAtUtc,
        };
    }

    private static BasketPageItemDto MapItem(
        BasketItemDto item,
        Dictionary<Guid, CatalogProductPricingDto>? productsById,
        Dictionary<Guid, BulkStockLevelDto>? availabilityById)
    {
        var snapshotPrice = new MoneyDto(item.SnapshotPrice.Amount, item.SnapshotPrice.Currency);

        CatalogProductPricingDto? product = null;
        if (productsById is not null && productsById.TryGetValue(item.ProductId, out var foundProduct))
        {
            product = foundProduct;
        }

        var currentPrice = product is null ? null : new MoneyDto(product.Price.Amount, product.Price.Currency);
        var priceDrifted = currentPrice is not null
            && (currentPrice.Amount != snapshotPrice.Amount || currentPrice.Currency != snapshotPrice.Currency);

        int? available = null;
        if (availabilityById is not null && availabilityById.TryGetValue(item.ProductId, out var stock))
        {
            available = stock.Available;
        }

        var lineTotalSnapshot = new MoneyDto(snapshotPrice.Amount * item.Quantity, snapshotPrice.Currency);
        var effectiveCurrent = currentPrice ?? snapshotPrice;
        var lineTotalCurrent = new MoneyDto(effectiveCurrent.Amount * item.Quantity, effectiveCurrent.Currency);

        return new BasketPageItemDto
        {
            ProductId = item.ProductId,
            Sku = item.Sku,
            Name = item.Name,
            Quantity = item.Quantity,
            SnapshotPrice = snapshotPrice,
            CurrentPrice = currentPrice,
            PriceDrifted = priceDrifted,
            LineTotalSnapshot = lineTotalSnapshot,
            LineTotalCurrent = lineTotalCurrent,
            AvailableQty = available,
            OutOfStock = available is not null && available < item.Quantity,
            PrimaryImageUrl = product is null ? null : PrimaryImageUrl(product),
        };
    }

    private static string? PrimaryImageUrl(CatalogProductPricingDto product) =>
        product.Images.Count == 0
            ? null
            : product.Images.OrderBy(image => image.DisplayOrder).First().Url;
}
