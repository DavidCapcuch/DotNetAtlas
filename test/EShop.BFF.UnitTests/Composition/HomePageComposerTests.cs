using EShop.BFF.Api.Composition;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;

namespace EShop.BFF.UnitTests.Composition;

/// <summary>
/// Partial-success matrix for the pure home-page composition (bff.md § 3.4). A successful Catalog
/// search is the composer's precondition (Catalog-search gating lives in the endpoint); the variation
/// under test is the category-tree pass-through and the Inventory bulk overlay.
/// </summary>
public sealed class HomePageComposerTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 06, 15, 10, 30, 00, TimeSpan.Zero);

    private static readonly Guid LaptopId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MouseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Compose_WhenCategoryTreeAndStockPresent_MergesAvailabilityAndIsNotStale()
    {
        // Arrange
        var featured = new[] { Summary(LaptopId, "Laptop"), Summary(MouseId, "Mouse") };
        var tree = Tree();
        var stock = Bulk((LaptopId, 7), (MouseId, 0));

        // Act
        var response = HomePageComposer.Compose(featured, tree, stock, GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.HasStaleData.Should().BeFalse();
            response.GeneratedAtUtc.Should().Be(GeneratedAt);
            response.CategoryTree.Should().NotBeNull();
            response.CategoryTree!.Nodes.Should().ContainSingle();

            var laptop = response.FeaturedProducts.Single(p => p.ProductId == LaptopId);
            laptop.InStock.Should().BeTrue();
            laptop.AvailableQty.Should().Be(7);

            var mouse = response.FeaturedProducts.Single(p => p.ProductId == MouseId);
            mouse.InStock.Should().BeFalse();
            mouse.AvailableQty.Should().Be(0);
        }
    }

    [Fact]
    public void Compose_MapsAllFeaturedProductFieldsFromSearch()
    {
        // Arrange
        var featured = new[] { Summary(LaptopId, "Laptop") };

        // Act
        var response = HomePageComposer.Compose(featured, Tree(), Bulk((LaptopId, 3)), GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            var product = response.FeaturedProducts.Single();
            product.Sku.Should().Be("SKU-Laptop");
            product.Name.Should().Be("Laptop");
            product.BrandName.Should().Be("Acme");
            product.CategoryBreadcrumb.Should().Be("Electronics > Computers");
            product.Price.Amount.Should().Be(1299.99m);
            product.Price.Currency.Should().Be("USD");
            product.PrimaryImageUrl.Should().Be("https://cdn/Laptop.jpg");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public void Compose_WhenCategoryTreeUnavailable_KeepsFeaturedButNullsTreeAndIsStale()
    {
        // Arrange
        var featured = new[] { Summary(LaptopId, "Laptop") };

        // Act — null tree models an unavailable Catalog category-tree read (bff.md § 3.4 failure table).
        var response = HomePageComposer.Compose(featured, categoryTreeOrNull: null, Bulk((LaptopId, 5)), GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.CategoryTree.Should().BeNull();
            response.FeaturedProducts.Should().ContainSingle();
            response.FeaturedProducts.Single().AvailableQty.Should().Be(5);
            response.HasStaleData.Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public void Compose_WhenInventoryUnavailable_NullsAvailabilityAndHighlightsAndIsStale()
    {
        // Arrange
        var featured = new[] { Summary(LaptopId, "Laptop") };

        // Act — null stock models an unavailable Inventory bulk overlay (bff.md § 3.4 failure table).
        var response = HomePageComposer.Compose(featured, Tree(), stockOrNull: null, GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.FeaturedProducts.Single().InStock.Should().BeNull();
            response.FeaturedProducts.Single().AvailableQty.Should().BeNull();
            response.StockHighlights.Should().BeNull();
            response.HasStaleData.Should().BeTrue();
        }
    }

    [Fact]
    public void Compose_DerivesStockHighlights_FromRunningLowProductsOnly()
    {
        // Arrange — running low (0 < qty <= 10) is only the mouse; the laptop (qty 11) and an
        // out-of-stock keyboard (qty 0) are excluded (bff.md § 3.4).
        var keyboardId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var featured = new[] { Summary(LaptopId, "Laptop"), Summary(MouseId, "Mouse"), Summary(keyboardId, "Keyboard") };
        var stock = Bulk((LaptopId, 11), (MouseId, 4), (keyboardId, 0));

        // Act
        var response = HomePageComposer.Compose(featured, Tree(), stock, GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.StockHighlights.Should().ContainSingle();
            var highlight = response.StockHighlights!.Single();
            highlight.ProductId.Should().Be(MouseId);
            highlight.Name.Should().Be("Mouse");
            highlight.AvailableQty.Should().Be(4);
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public void Compose_WhenProductMissingFromBulk_NullsThatItemsAvailability()
    {
        // Arrange — the mouse has no initialized stock item, so the bulk read omits it; the overlay
        // itself succeeded, so the page is not globally stale (bff.md § 3.4).
        var featured = new[] { Summary(LaptopId, "Laptop"), Summary(MouseId, "Mouse") };
        var stock = Bulk((LaptopId, 7));

        // Act
        var response = HomePageComposer.Compose(featured, Tree(), stock, GeneratedAt);

        // Assert
        using (new AssertionScope())
        {
            response.FeaturedProducts.Single(p => p.ProductId == LaptopId).AvailableQty.Should().Be(7);
            var mouse = response.FeaturedProducts.Single(p => p.ProductId == MouseId);
            mouse.InStock.Should().BeNull();
            mouse.AvailableQty.Should().BeNull();
            response.HasStaleData.Should().BeFalse();
        }
    }

    private static CatalogProductSummaryDto Summary(Guid productId, string name) =>
        new()
        {
            ProductId = productId,
            Sku = $"SKU-{name}",
            Name = name,
            CategoryBreadcrumb = "Electronics > Computers",
            BrandName = "Acme",
            Price = new CatalogMoneyDto { Amount = 1299.99m, Currency = "USD" },
            Status = "Active",
            PrimaryImageUrl = $"https://cdn/{name}.jpg",
        };

    private static CategoryTreeDto Tree() =>
        new()
        {
            Nodes =
            [
                new CategoryNodeDto
                {
                    CategoryId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    Name = "Electronics",
                    Path = "/electronics",
                    ParentCategoryId = null,
                    Depth = 0,
                    ProductCount = 12,
                },
            ],
        };

    private static StockLevelsBulkDto Bulk(params (Guid ProductId, int Available)[] items) =>
        new()
        {
            Items = items
                .Select(i => new BulkStockLevelDto { ProductId = i.ProductId, Available = i.Available })
                .ToList(),
        };
}
