using Catalog.Application.Products.DescribeProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Products.DescribeProduct;

public class DescribeProductCommandHandlerTests
{
    [Fact]
    public async Task Given_ActiveProduct_When_Describing_Then_PersistsAndRaisesEvent()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DescribeProductCommandHandler(
            db, NullLogger<DescribeProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new DescribeProductCommand
            {
                ProductId = product.Id,
                NewDescription = "Refreshed description.",
            },
            TestContext.Current.CancellationToken);

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
    public async Task Given_DiscontinuedProduct_When_Describing_Then_Fails()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DiscontinuedProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DescribeProductCommandHandler(
            db, NullLogger<DescribeProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new DescribeProductCommand
            {
                ProductId = product.Id,
                NewDescription = "x",
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Given_MissingProduct_When_Describing_Then_FailsNotFound()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new DescribeProductCommandHandler(
            db, NullLogger<DescribeProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new DescribeProductCommand
            {
                ProductId = Guid.CreateVersion7(),
                NewDescription = "x",
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("does not exist"));
    }
}
