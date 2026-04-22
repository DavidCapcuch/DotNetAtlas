using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.SharedKernel.Exceptions;

namespace Catalog.UnitTests.Products.CreateProduct;

public class ProductCreatedProjectionHandlerTests
{
    [Fact]
    public async Task Given_TrackedProductAndCategory_When_Handling_Then_InsertsProjectionRow()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DraftProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductCreatedProjectionHandler(
            db, NullLogger<ProductCreatedProjectionHandler>.Instance);

        var domainEvent = new ProductCreatedDomainEvent
        {
            ProductId = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            CategoryId = product.CategoryId,
            Price = product.Price,
        };

        await handler.Handle(domainEvent, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var row = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == product.Id, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            row.Sku.Should().Be("TEST-001");
            row.CategoryPath.Should().Be(category.Path.Value);
            row.CategoryBreadcrumb.Should().Be("Electronics");
            row.PriceAmount.Should().Be(9.99m);
            row.PriceCurrency.Should().Be("USD");
            row.Status.Should().Be("Draft");
            row.IsSellable.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Given_MissingProduct_When_Handling_Then_ThrowsDataIntegrityException()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ProductCreatedProjectionHandler(
            db, NullLogger<ProductCreatedProjectionHandler>.Instance);

        var domainEvent = new ProductCreatedDomainEvent
        {
            ProductId = Guid.CreateVersion7(),
            Sku = CatalogFactories.DraftProduct().Sku,
            Name = CatalogFactories.DraftProduct().Name,
            CategoryId = Guid.CreateVersion7(),
            Price = CatalogFactories.DraftProduct().Price,
        };

        var action = async () => await handler.Handle(domainEvent, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<DataIntegrityException>();
    }
}
