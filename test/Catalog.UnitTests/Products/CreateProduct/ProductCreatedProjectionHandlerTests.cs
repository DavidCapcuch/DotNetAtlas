using System.Diagnostics;
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
            OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
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
        // CAT-TST-M02 (Wave-1 closeout): build the event from a single product instance so
        // Sku / Name / Price come from the same aggregate — the previous version called
        // CatalogFactories.DraftProduct() three times, returning three diverging instances.
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ProductCreatedProjectionHandler(
            db, NullLogger<ProductCreatedProjectionHandler>.Instance);

        var template = CatalogFactories.DraftProduct();
        var domainEvent = new ProductCreatedDomainEvent
        {
            OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
            ProductId = Guid.CreateVersion7(),
            Sku = template.Sku,
            Name = template.Name,
            CategoryId = Guid.CreateVersion7(),
            Price = template.Price,
        };

        var action = async () => await handler.Handle(domainEvent, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<DataIntegrityException>();
    }

    /// <summary>
    /// CAT-RV-C01 (Wave-1 closeout): the projected row hard-coded CorrelationId = Guid.Empty
    /// despite the AddCorrelationId middleware already publishing the value to the ambient
    /// Activity tag. The projection handler must read it from Activity.Current so the
    /// product_search_view row carries the actual correlation id for the originating request.
    /// </summary>
    [Fact]
    public async Task Given_CorrelationIdOnActivity_When_Handling_Then_ProjectionCarriesIt()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DraftProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var correlationId = Guid.CreateVersion7();
        using var activitySource = new ActivitySource("Catalog.Test");
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(activityListener);
        using var activity = activitySource.StartActivity("test");
        // Mirrors Platform.ServiceDefaults.CorrelationId.CorrelationIdContextKeys.ActivityTagName.
        activity!.SetTag("correlation.id", correlationId.ToString());

        var handler = new ProductCreatedProjectionHandler(
            db, NullLogger<ProductCreatedProjectionHandler>.Instance);
        var domainEvent = new ProductCreatedDomainEvent
        {
            OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
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
        row.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task Given_NoActivity_When_Handling_Then_ProjectionCarriesEmptyCorrelationId()
    {
        // Background / inbox-driven flows have no HTTP request and no Activity tag — fall
        // back to Guid.Empty so the row still inserts (we don't manufacture a fake id).
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DraftProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Activity.Current = null;

        var handler = new ProductCreatedProjectionHandler(
            db, NullLogger<ProductCreatedProjectionHandler>.Instance);
        var domainEvent = new ProductCreatedDomainEvent
        {
            OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
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
        row.CorrelationId.Should().Be(Guid.Empty);
    }
}
