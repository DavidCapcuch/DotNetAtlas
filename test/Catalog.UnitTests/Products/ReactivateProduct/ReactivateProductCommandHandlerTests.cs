using Catalog.Application.Products.ReactivateProduct;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Products.ReactivateProduct;

public class ReactivateProductCommandHandlerTests
{
    [Fact]
    public async Task Given_DiscontinuedProduct_When_AdminReactivating_Then_BecomesActive()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DiscontinuedProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ReactivateProductCommandHandler(
            db, TimeProvider.System, NullLogger<ReactivateProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReactivateProductCommand { ProductId = product.Id, AdminReactivation = true },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);
            refreshed.Status.Should().Be(ProductStatus.Active);
            refreshed.PopDomainEvents().OfType<ProductReactivatedDomainEvent>()
                .Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Given_DiscontinuedProduct_When_WithoutAdminFlag_Then_Fails()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DiscontinuedProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ReactivateProductCommandHandler(
            db, TimeProvider.System, NullLogger<ReactivateProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReactivateProductCommand { ProductId = product.Id, AdminReactivation = false },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Given_MissingProduct_Then_FailsNotFound()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ReactivateProductCommandHandler(
            db, TimeProvider.System, NullLogger<ReactivateProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReactivateProductCommand
            {
                ProductId = Guid.CreateVersion7(),
                AdminReactivation = true,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }
}
