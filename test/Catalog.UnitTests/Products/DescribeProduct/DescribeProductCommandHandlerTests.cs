using Catalog.Application.Products.DescribeProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.DescribeProduct;

public class DescribeProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_ActiveProduct_PersistsAndRaisesEvent()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DescribeProductCommandHandler(
            db, TimeProvider.System, NullLogger<DescribeProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new DescribeProductCommand
            {
                ProductId = product.Id,
                NewDescription = "Refreshed description.",
            },
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);
            refreshed.Description.Value.Should().Be("Refreshed description.");
            refreshed.PopDomainEvents().OfType<ProductDescribedDomainEvent>()
                .Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Handle_DiscontinuedProduct_Fails()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DiscontinuedProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DescribeProductCommandHandler(
            db, TimeProvider.System, NullLogger<DescribeProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new DescribeProductCommand
            {
                ProductId = product.Id,
                NewDescription = "x",
            },
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
    }

    [Fact]
    public async Task Handle_MissingProduct_FailsNotFound()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var handler = new DescribeProductCommandHandler(
            db, TimeProvider.System, NullLogger<DescribeProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new DescribeProductCommand
            {
                ProductId = Guid.CreateVersion7(),
                NewDescription = "x",
            },
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e => ((DomainError)e).ErrorCode == "Product.NotFound");
    }
}
