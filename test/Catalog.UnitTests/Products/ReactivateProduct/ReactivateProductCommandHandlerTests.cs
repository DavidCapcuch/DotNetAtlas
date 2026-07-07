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
    public async Task Handle_DiscontinuedProductWithAdminReactivation_BecomesActive()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DiscontinuedProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ReactivateProductCommandHandler(
            db, TimeProvider.System, NullLogger<ReactivateProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new ReactivateProductCommand { ProductId = product.Id, AdminReactivation = true },
            TestContext.Current.CancellationToken);

        // Assert
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
    public async Task Handle_DiscontinuedProductWithoutAdminFlag_Fails()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DiscontinuedProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ReactivateProductCommandHandler(
            db, TimeProvider.System, NullLogger<ReactivateProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new ReactivateProductCommand { ProductId = product.Id, AdminReactivation = false },
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
    }

    [Fact]
    public async Task Handle_MissingProduct_FailsNotFound()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ReactivateProductCommandHandler(
            db, TimeProvider.System, NullLogger<ReactivateProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new ReactivateProductCommand
            {
                ProductId = Guid.CreateVersion7(),
                AdminReactivation = true,
            },
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
    }
}
