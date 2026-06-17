using EShop.BFF.Api.Composition;
using EShop.BFF.Infrastructure.Clients.Basket;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;

namespace EShop.BFF.UnitTests.Composition;

/// <summary>
/// Matrix for the pure basket-page composition (bff.md § 3.2): snapshot lines overlaid with current
/// Catalog price (drift) + current Inventory availability (out-of-stock), with batch-failure / partial
/// degradation flagged via <c>HasStaleData</c>.
/// </summary>
public sealed class BasketPageComposerTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 06, 17, 10, 30, 00, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ProductA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProductB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Compose_WhenPriceUnchangedAndInStock_NoDriftNoOutOfStockNotStale()
    {
        // Arrange — snapshot 10.00 USD ×2, current 10.00, available 5 ≥ 2.
        var basket = BuildBasket(BuildItem(ProductA, snapshot: 10.00m, quantity: 2));
        var catalog = BuildCatalog(BuildProduct(ProductA, current: 10.00m));
        var inventory = BuildInventory(BuildStock(ProductA, available: 5));

        // Act
        var response = BasketPageComposer.Compose(basket, catalog, inventory, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        var item = response.Items.Should().ContainSingle().Subject;
        item.SnapshotPrice.Amount.Should().Be(10.00m);
        item.CurrentPrice!.Amount.Should().Be(10.00m);
        item.PriceDrifted.Should().BeFalse();
        item.AvailableQty.Should().Be(5);
        item.OutOfStock.Should().BeFalse();
        item.LineTotalSnapshot.Amount.Should().Be(20.00m);
        item.LineTotalCurrent.Amount.Should().Be(20.00m);
        response.TotalSnapshot.Amount.Should().Be(20.00m);
        response.TotalCurrent.Amount.Should().Be(20.00m);
        response.HasPriceDrift.Should().BeFalse();
        response.HasOutOfStock.Should().BeFalse();
        response.HasStaleData.Should().BeFalse();
        response.UserId.Should().Be(UserId);
        response.GeneratedAtUtc.Should().Be(GeneratedAt);
    }

    [Fact]
    public void Compose_WhenCurrentPriceDiffersFromSnapshot_FlagsDriftAndUsesCurrentForLineTotalCurrent()
    {
        // Arrange — snapshot 10.00, current 12.50 (drift up), ×3.
        var basket = BuildBasket(BuildItem(ProductA, snapshot: 10.00m, quantity: 3));
        var catalog = BuildCatalog(BuildProduct(ProductA, current: 12.50m));
        var inventory = BuildInventory(BuildStock(ProductA, available: 10));

        // Act
        var response = BasketPageComposer.Compose(basket, catalog, inventory, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        var item = response.Items.Should().ContainSingle().Subject;
        item.PriceDrifted.Should().BeTrue();
        item.LineTotalSnapshot.Amount.Should().Be(30.00m);
        item.LineTotalCurrent.Amount.Should().Be(37.50m);
        response.TotalSnapshot.Amount.Should().Be(30.00m);
        response.TotalCurrent.Amount.Should().Be(37.50m);
        response.HasPriceDrift.Should().BeTrue();
        response.HasStaleData.Should().BeFalse();
    }

    [Fact]
    public void Compose_WhenAvailableBelowQuantity_FlagsOutOfStock()
    {
        // Arrange — quantity 4, only 1 available.
        var basket = BuildBasket(BuildItem(ProductA, snapshot: 10.00m, quantity: 4));
        var catalog = BuildCatalog(BuildProduct(ProductA, current: 10.00m));
        var inventory = BuildInventory(BuildStock(ProductA, available: 1));

        // Act
        var response = BasketPageComposer.Compose(basket, catalog, inventory, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        var item = response.Items.Should().ContainSingle().Subject;
        item.AvailableQty.Should().Be(1);
        item.OutOfStock.Should().BeTrue();
        response.HasOutOfStock.Should().BeTrue();
        response.HasStaleData.Should().BeFalse();
    }

    [Fact]
    public void Compose_WhenCatalogBatchUnavailable_NullsCurrentPriceFallsBackToSnapshotAndFlagsStale()
    {
        // Arrange — Catalog batch failed (null); Inventory healthy.
        var basket = BuildBasket(BuildItem(ProductA, snapshot: 10.00m, quantity: 2));
        var inventory = BuildInventory(BuildStock(ProductA, available: 5));

        // Act
        var response = BasketPageComposer.Compose(basket, catalogOrNull: null, inventory, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        var item = response.Items.Should().ContainSingle().Subject;
        item.CurrentPrice.Should().BeNull();
        item.PriceDrifted.Should().BeFalse();
        item.LineTotalCurrent.Amount.Should().Be(20.00m, "current falls back to snapshot when Catalog is down");
        response.TotalCurrent.Amount.Should().Be(20.00m);
        response.HasPriceDrift.Should().BeFalse();
        response.HasStaleData.Should().BeTrue();
    }

    [Fact]
    public void Compose_WhenInventoryBatchUnavailable_NullsAvailabilityNoOutOfStockAndFlagsStale()
    {
        // Arrange — Inventory batch failed (null); Catalog healthy.
        var basket = BuildBasket(BuildItem(ProductA, snapshot: 10.00m, quantity: 2));
        var catalog = BuildCatalog(BuildProduct(ProductA, current: 10.00m));

        // Act
        var response = BasketPageComposer.Compose(basket, catalog, inventoryOrNull: null, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        var item = response.Items.Should().ContainSingle().Subject;
        item.AvailableQty.Should().BeNull();
        item.OutOfStock.Should().BeFalse();
        response.HasOutOfStock.Should().BeFalse();
        response.HasStaleData.Should().BeTrue();
    }

    [Fact]
    public void Compose_WhenCatalogOmitsProduct_NullsThatItemsCurrentPriceAndFlagsStale()
    {
        // Arrange — Catalog batch succeeded but returned no row for ProductA (discontinued / missing).
        var basket = BuildBasket(BuildItem(ProductA, snapshot: 10.00m, quantity: 1));
        var catalog = new CatalogProductsByIdsDto(Products: [], MissingProductIds: [ProductA]);
        var inventory = BuildInventory(BuildStock(ProductA, available: 5));

        // Act
        var response = BasketPageComposer.Compose(basket, catalog, inventory, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        var item = response.Items.Should().ContainSingle().Subject;
        item.CurrentPrice.Should().BeNull();
        item.PriceDrifted.Should().BeFalse();
        response.HasStaleData.Should().BeTrue();
    }

    [Fact]
    public void Compose_WhenInventoryOmitsProduct_NullsThatItemsAvailabilityAndFlagsStale()
    {
        // Arrange — Inventory batch succeeded but ProductA had no initialized stock item.
        var basket = BuildBasket(BuildItem(ProductA, snapshot: 10.00m, quantity: 1));
        var catalog = BuildCatalog(BuildProduct(ProductA, current: 10.00m));
        var inventory = new StockLevelsBulkDto(Items: [], MissingProductIds: [ProductA]);

        // Act
        var response = BasketPageComposer.Compose(basket, catalog, inventory, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        var item = response.Items.Should().ContainSingle().Subject;
        item.AvailableQty.Should().BeNull();
        item.OutOfStock.Should().BeFalse();
        response.HasStaleData.Should().BeTrue();
    }

    [Fact]
    public void Compose_WhenBasketEmpty_ReturnsEmptyItemsZeroTotalsNotStale()
    {
        // Arrange
        var basket = BuildBasket();

        // Act — no enrichment performed for an empty basket.
        var response = BasketPageComposer.Compose(basket, catalogOrNull: null, inventoryOrNull: null, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        response.Items.Should().BeEmpty();
        response.TotalSnapshot.Amount.Should().Be(0m);
        response.TotalCurrent.Amount.Should().Be(0m);
        response.HasPriceDrift.Should().BeFalse();
        response.HasOutOfStock.Should().BeFalse();
        response.HasStaleData.Should().BeFalse();
    }

    [Fact]
    public void Compose_WithMultipleItems_SumsSnapshotAndCurrentTotalsIndependently()
    {
        // Arrange — A: snapshot 10 ×2 (current 11), B: snapshot 5 ×3 (current 5).
        var basket = BuildBasket(
            BuildItem(ProductA, snapshot: 10.00m, quantity: 2),
            BuildItem(ProductB, snapshot: 5.00m, quantity: 3));
        var catalog = BuildCatalog(
            BuildProduct(ProductA, current: 11.00m),
            BuildProduct(ProductB, current: 5.00m));
        var inventory = BuildInventory(
            BuildStock(ProductA, available: 9),
            BuildStock(ProductB, available: 9));

        // Act
        var response = BasketPageComposer.Compose(basket, catalog, inventory, GeneratedAt);

        // Assert — snapshot 20 + 15 = 35; current 22 + 15 = 37.
        using var _ = new AssertionScope();
        response.Items.Should().HaveCount(2);
        response.TotalSnapshot.Amount.Should().Be(35.00m);
        response.TotalCurrent.Amount.Should().Be(37.00m);
        response.HasPriceDrift.Should().BeTrue();
    }

    [Fact]
    public void Compose_TakesPrimaryImageUrlFromLowestDisplayOrderCatalogImage()
    {
        // Arrange — two images out of order; DisplayOrder 0 is primary.
        var basket = BuildBasket(BuildItem(ProductA, snapshot: 10.00m, quantity: 1));
        var product = BuildProduct(ProductA, current: 10.00m) with
        {
            Images =
            [
                new CatalogImageDto("https://cdn/secondary.jpg", "secondary", 1),
                new CatalogImageDto("https://cdn/primary.jpg", "primary", 0),
            ],
        };
        var catalog = BuildCatalog(product);
        var inventory = BuildInventory(BuildStock(ProductA, available: 5));

        // Act
        var response = BasketPageComposer.Compose(basket, catalog, inventory, GeneratedAt);

        // Assert
        response.Items.Single().PrimaryImageUrl.Should().Be("https://cdn/primary.jpg");
    }

    private static BasketDto BuildBasket(params BasketItemDto[] items) =>
        new(
            UserId: UserId,
            Version: 4,
            Items: items,
            Total: items.Length == 0 ? null : new BasketMoneyDto(items.Sum(i => i.LineTotal.Amount), "USD"),
            CreatedAtUtc: GeneratedAt,
            LastModifiedAtUtc: GeneratedAt);

    private static BasketItemDto BuildItem(Guid productId, decimal snapshot, int quantity) =>
        new(
            ProductId: productId,
            Sku: $"SKU-{productId.ToString()[..4]}",
            Name: $"Product {productId.ToString()[..4]}",
            SnapshotPrice: new BasketMoneyDto(snapshot, "USD"),
            Quantity: quantity,
            CapturedAtUtc: GeneratedAt,
            LineTotal: new BasketMoneyDto(snapshot * quantity, "USD"));

    private static CatalogProductsByIdsDto BuildCatalog(params CatalogProductDto[] products) =>
        new(Products: products, MissingProductIds: []);

    private static CatalogProductDto BuildProduct(Guid productId, decimal current) =>
        new(
            ProductId: productId,
            Sku: $"SKU-{productId.ToString()[..4]}",
            Name: $"Product {productId.ToString()[..4]}",
            Description: "desc",
            BrandName: "Acme",
            CategoryPath: "/c",
            CategoryBreadcrumb: "C",
            Price: new CatalogMoneyDto(current, "USD"),
            Status: "Active",
            Dimensions: null,
            Images: [new CatalogImageDto("https://cdn/img.jpg", "img", 0)]);

    private static StockLevelsBulkDto BuildInventory(params BulkStockLevelDto[] items) =>
        new(Items: items, MissingProductIds: []);

    private static BulkStockLevelDto BuildStock(Guid productId, int available) =>
        new(
            ProductId: productId,
            OnHand: available + 2,
            Reserved: 2,
            Available: available,
            LastUpdatedUtc: GeneratedAt);
}
