using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.SharedKernel.Exceptions;

namespace Catalog.UnitTests.Products.CreateProduct;

public class ProductCreatedProjectionDomainEventHandlerTests
{
    [Fact]
    public async Task Handle_TrackedProductAndCategory_InsertsProjectionRow()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductCreatedProjectionDomainEventHandler(
            db, NullLogger<ProductCreatedProjectionDomainEventHandler>.Instance);

        var domainEvent = new ProductCreatedDomainEvent
        {
            OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
            ProductId = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            CategoryId = product.CategoryId,
            Price = product.Price,
        };

        // Act
        await handler.Handle(domainEvent, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var row = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == product.Id, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            row.Sku.Should().Be("TEST-001");
            row.CategoryPath.Should().Be(category.Path.Value);
            row.CategoryBreadcrumb.Should().Be("Electronics");
            row.PriceAmount.Should().Be(9.99m);
            row.PriceCurrency.Should().Be("USD");
            row.Status.Should().Be("Active");
            row.IsSellable.Should().BeTrue();
        }
    }

    // CAT-RV-L01 (Wave-1 closeout): category slug segments containing hyphens between words
    // ("electronics-toys") must title-case each space-delimited token, producing
    // "Electronics Toys" rather than the broken "Electronics-toys".
    [Fact]
    public async Task Handle_HyphenatedCategorySlug_BreadcrumbSplitsAndTitleCasesTokens()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory("Electronics Toys");
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductCreatedProjectionDomainEventHandler(
            db, NullLogger<ProductCreatedProjectionDomainEventHandler>.Instance);
        var domainEvent = new ProductCreatedDomainEvent
        {
            OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
            ProductId = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            CategoryId = product.CategoryId,
            Price = product.Price,
        };

        // Act
        await handler.Handle(domainEvent, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var row = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == product.Id, TestContext.Current.CancellationToken);
        row.CategoryBreadcrumb.Should().Be("Electronics Toys");
    }

    [Fact]
    public async Task Handle_MissingProduct_ThrowsDataIntegrityException()
    {
        // CAT-TST-M02 (Wave-1 closeout): build the event from a single product instance so
        // Sku / Name / Price come from the same aggregate — the previous version called
        // CatalogFactories.ActiveProduct() three times, returning three diverging instances.
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ProductCreatedProjectionDomainEventHandler(
            db, NullLogger<ProductCreatedProjectionDomainEventHandler>.Instance);

        var template = CatalogFactories.ActiveProduct();
        var domainEvent = new ProductCreatedDomainEvent
        {
            OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
            ProductId = Guid.CreateVersion7(),
            Sku = template.Sku,
            Name = template.Name,
            CategoryId = Guid.CreateVersion7(),
            Price = template.Price,
        };

        // Act
        var action = async () => await handler.Handle(domainEvent, TestContext.Current.CancellationToken);

        // Assert
        await action.Should().ThrowAsync<DataIntegrityException>();
    }
}
