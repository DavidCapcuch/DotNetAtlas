using Catalog.Application.Products.DiscontinueProduct;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Products.DiscontinueProduct;

public class DiscontinueProductCommandHandlerTests
{
    [Fact]
    public async Task Given_ActiveProduct_When_Discontinuing_Then_SucceedsAndRaisesEvent()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DiscontinueProductCommandHandler(
            db, TimeProvider.System, NullLogger<DiscontinueProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new DiscontinueProductCommand { ProductId = product.Id, Reason = "EOL" },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);
            refreshed.Status.Should().Be(ProductStatus.Discontinued);
            refreshed.PopDomainEvents().OfType<ProductDiscontinuedDomainEvent>()
                .Should().ContainSingle().Which.Reason.Should().Be("EOL");
        }
    }

    [Fact]
    public async Task Given_MissingProduct_Then_FailsNotFound()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new DiscontinueProductCommandHandler(
            db, TimeProvider.System, NullLogger<DiscontinueProductCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new DiscontinueProductCommand { ProductId = Guid.CreateVersion7(), Reason = "x" },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }
}
