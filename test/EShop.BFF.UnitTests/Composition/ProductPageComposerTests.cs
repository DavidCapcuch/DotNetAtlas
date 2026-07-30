using EShop.BFF.Api.Composition;
using EShop.BFF.Infrastructure.Clients.Basket;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;

namespace EShop.BFF.UnitTests.Composition;

/// <summary>
/// Partial-success matrix for the pure product-page composition (bff.md § 3.1). Catalog success is
/// the composer's precondition; the variation under test is the Inventory enrichment.
/// </summary>
public sealed class ProductPageComposerTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 06, 15, 10, 30, 00, TimeSpan.Zero);

    private static readonly Guid ProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Compose_WhenInventoryHasStock_ReturnsInStockWithQuantityAndNotStale()
    {
        // Arrange
        var product = BuildProduct();
        var stock = BuildStock(available: 7);

        // Act
        var response = ProductPageComposer.Compose(product, stock, GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.InStock.Should().BeTrue();
            response.AvailableQty.Should().Be(7);
            response.HasStaleData.Should().BeFalse();
            response.GeneratedAtUtc.Should().Be(GeneratedAt);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Compose_WhenInventoryOutOfStock_ReturnsNotInStockWithZeroQuantityAndNotStale()
    {
        // Arrange
        var product = BuildProduct();
        var stock = BuildStock(available: 0);

        // Act
        var response = ProductPageComposer.Compose(product, stock, GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.InStock.Should().BeFalse();
            response.AvailableQty.Should().Be(0);
            response.HasStaleData.Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public void Compose_WhenInventoryUnavailable_ReturnsNullAvailabilityAndStale()
    {
        // Arrange
        var product = BuildProduct();

        // Act — null stock models an unavailable Inventory (timeout / 5xx / 404).
        var response = ProductPageComposer.Compose(product, stockOrNull: null, GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.InStock.Should().BeNull();
            response.AvailableQty.Should().BeNull();
            response.HasStaleData.Should().BeTrue();
        }
    }

    [Fact]
    public void Compose_WithFullCatalogProduct_MapsAllProductFields()
    {
        // Arrange
        var product = BuildProduct();
        var stock = BuildStock(available: 3);

        // Act
        var response = ProductPageComposer.Compose(product, stock, GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.Product.ProductId.Should().Be(ProductId);
            response.Product.Sku.Should().Be("SKU-1");
            response.Product.Name.Should().Be("Laptop");
            response.Product.Description.Should().Be("A fast laptop");
            response.Product.BrandName.Should().Be("Acme");
            response.Product.CategoryBreadcrumb.Should().Be("Electronics > Computers > Laptops");
            response.Product.CategoryPath.Should().Be("/electronics/computers/laptops");
            response.Product.Status.Should().Be("Active");
            response.Product.Price.Amount.Should().Be(1299.99m);
            response.Product.Price.Currency.Should().Be("USD");
            response.Product.Dimensions.Should().NotBeNull();
            response.Product.Dimensions!.Unit.Should().Be("cm");
            response.Product.Images.Should().ContainSingle();
            response.Product.Images[0].Url.Should().Be("https://cdn/img-1.jpg");
            response.Product.Images[0].DisplayOrder.Should().Be(0);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Compose_WhenProductHasNoDimensions_MapsDimensionsToNull()
    {
        // Arrange
        var product = BuildProduct() with { Dimensions = null };
        var stock = BuildStock(available: 1);

        // Act
        var response = ProductPageComposer.Compose(product, stock, GeneratedAt);

        // Assert
        response.Product.Dimensions.Should().BeNull();
    }

    [Fact]
    public void WithBasketOverlay_WhenProductInBasket_SetsAlreadyInBasketTrueWithQuantity()
    {
        // Arrange — the anonymous cached page, then the per-request buyer overlay (bff.md § 3.1).
        var page = ProductPageComposer.Compose(BuildProduct(), BuildStock(available: 5), GeneratedAt);
        var basket = BuildBasket(BasketItem(ProductId, quantity: 4));

        // Act
        var overlaid = ProductPageComposer.WithBasketOverlay(page, basket, ProductId);

        // Assert
        using (new AssertionScope())
        {
            overlaid.AlreadyInBasket.Should().BeTrue();
            overlaid.BasketQuantity.Should().Be(4);
        }
    }

    [Fact]
    public void WithBasketOverlay_WhenProductNotInBasket_SetsAlreadyInBasketFalseWithNullQuantity()
    {
        // Arrange — basket holds a different product.
        var page = ProductPageComposer.Compose(BuildProduct(), BuildStock(available: 5), GeneratedAt);
        var basket = BuildBasket(BasketItem(Guid.Parse("22222222-2222-2222-2222-222222222222"), quantity: 1));

        // Act
        var overlaid = ProductPageComposer.WithBasketOverlay(page, basket, ProductId);

        // Assert
        using (new AssertionScope())
        {
            overlaid.AlreadyInBasket.Should().BeFalse();
            overlaid.BasketQuantity.Should().BeNull();
        }
    }

    [Fact]
    public void WithBasketOverlay_WhenApplied_LeavesTheCachedProductAndAvailabilityUntouched()
    {
        // Arrange — the overlay must not disturb the shared anonymous composite.
        var page = ProductPageComposer.Compose(BuildProduct(), BuildStock(available: 5), GeneratedAt);
        var basket = BuildBasket(BasketItem(ProductId, quantity: 2));

        // Act
        var overlaid = ProductPageComposer.WithBasketOverlay(page, basket, ProductId);

        // Assert
        using (new AssertionScope())
        {
            overlaid.Product.Should().BeSameAs(page.Product);
            overlaid.InStock.Should().Be(page.InStock);
            overlaid.AvailableQty.Should().Be(page.AvailableQty);
            overlaid.HasStaleData.Should().Be(page.HasStaleData);
            overlaid.GeneratedAtUtc.Should().Be(page.GeneratedAtUtc);
        }
    }

    private static BasketDto BuildBasket(params BasketItemDto[] items) =>
        new()
        {
            UserId = Guid.NewGuid(),
            Version = 1,
            Items = items,
            Total = new BasketMoneyDto { Amount = 0m, Currency = "USD" },
        };

    private static BasketItemDto BasketItem(Guid productId, int quantity) =>
        new()
        {
            ProductId = productId,
            Sku = "SKU",
            Name = "Item",
            SnapshotPrice = new BasketMoneyDto { Amount = 10m, Currency = "USD" },
            Quantity = quantity,
        };

    private static CatalogProductDetailDto BuildProduct() =>
        new()
        {
            ProductId = ProductId,
            Sku = "SKU-1",
            Name = "Laptop",
            Description = "A fast laptop",
            BrandName = "Acme",
            CategoryPath = "/electronics/computers/laptops",
            CategoryBreadcrumb = "Electronics > Computers > Laptops",
            Price = new CatalogMoneyDto { Amount = 1299.99m, Currency = "USD" },
            Status = "Active",
            Dimensions = new CatalogDimensionsDto { Length = 35.5m, Width = 24.0m, Height = 2.0m, Unit = "cm" },
            Images = [new CatalogImageDto { Url = "https://cdn/img-1.jpg", AltText = "Laptop front", DisplayOrder = 0 }],
        };

    private static StockLevelDto BuildStock(int available) => new() { Available = available };
}
