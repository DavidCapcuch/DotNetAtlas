using EShop.BFF.Api.Composition;
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
        using var _ = new AssertionScope();
        response.InStock.Should().BeTrue();
        response.AvailableQty.Should().Be(7);
        response.HasStaleData.Should().BeFalse();
        response.GeneratedAtUtc.Should().Be(GeneratedAt);
    }

    [Fact]
    public void Compose_WhenInventoryOutOfStock_ReturnsNotInStockWithZeroQuantityAndNotStale()
    {
        // Arrange
        var product = BuildProduct();
        var stock = BuildStock(available: 0);

        // Act
        var response = ProductPageComposer.Compose(product, stock, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        response.InStock.Should().BeFalse();
        response.AvailableQty.Should().Be(0);
        response.HasStaleData.Should().BeFalse();
    }

    [Fact]
    public void Compose_WhenInventoryUnavailable_ReturnsNullAvailabilityAndStale()
    {
        // Arrange
        var product = BuildProduct();

        // Act — null stock models an unavailable Inventory (timeout / 5xx / 404).
        var response = ProductPageComposer.Compose(product, stockOrNull: null, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
        response.InStock.Should().BeNull();
        response.AvailableQty.Should().BeNull();
        response.HasStaleData.Should().BeTrue();
    }

    [Fact]
    public void Compose_MapsAllProductFieldsFromCatalog()
    {
        // Arrange
        var product = BuildProduct();
        var stock = BuildStock(available: 3);

        // Act
        var response = ProductPageComposer.Compose(product, stock, GeneratedAt);

        // Assert
        using var _ = new AssertionScope();
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

    [Fact]
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

    private static CatalogProductDto BuildProduct() =>
        new(
            ProductId: ProductId,
            Sku: "SKU-1",
            Name: "Laptop",
            Description: "A fast laptop",
            BrandName: "Acme",
            CategoryPath: "/electronics/computers/laptops",
            CategoryBreadcrumb: "Electronics > Computers > Laptops",
            Price: new CatalogMoneyDto(1299.99m, "USD"),
            Status: "Active",
            Dimensions: new CatalogDimensionsDto(35.5m, 24.0m, 2.0m, "cm"),
            Images: [new CatalogImageDto("https://cdn/img-1.jpg", "Laptop front", 0)]);

    private static StockLevelDto BuildStock(int available) =>
        new(
            ProductId: ProductId,
            OnHand: available + 2,
            Reserved: 2,
            Available: available,
            LastUpdatedUtc: GeneratedAt,
            LastVersion: 4);
}
