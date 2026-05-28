using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.UpdateProductPrice;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.UpdateProductPrice;

public class UpdateProductPriceCommandHandlerTests
{
    private static readonly DateTimeOffset FixedUtc =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Given_ActiveProduct_When_NewPriceDiffers_Then_UpdatesAndRaisesEvent()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var clock = new FakeTimeProvider(FixedUtc);
        var handler = new UpdateProductPriceCommandHandler(
            db, clock, NullLogger<UpdateProductPriceCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new UpdateProductPriceCommand
            {
                ProductId = product.Id,
                NewPrice = new MoneyDto { Amount = 42m, Currency = "USD" },
            },
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);
            refreshed.Price.Amount.Should().Be(42m);
            var raised = refreshed.PopDomainEvents().OfType<ProductPriceChangedDomainEvent>().Single();
            raised.NewPrice.Amount.Should().Be(42m);
            raised.OccurredOnUtc.Should().Be(FixedUtc);
        }
    }

    [Fact]
    public async Task Given_MissingProduct_When_Handling_Then_FailsWithNotFound()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new UpdateProductPriceCommandHandler(
            db, TimeProvider.System, NullLogger<UpdateProductPriceCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new UpdateProductPriceCommand
            {
                ProductId = Guid.CreateVersion7(),
                NewPrice = new MoneyDto { Amount = 1m, Currency = "USD" },
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e => ((DomainError)e).ErrorCode == "Product.NotFound");
    }

    [Fact]
    public async Task Given_DiscontinuedProduct_When_Handling_Then_FailsWithCannotReprice()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DiscontinuedProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new UpdateProductPriceCommandHandler(
            db, TimeProvider.System, NullLogger<UpdateProductPriceCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new UpdateProductPriceCommand
            {
                ProductId = product.Id,
                NewPrice = new MoneyDto { Amount = 42m, Currency = "USD" },
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("discontinued", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Given_IdenticalPrice_When_Handling_Then_SucceedsWithoutRaisingEvent()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new UpdateProductPriceCommandHandler(
            db, TimeProvider.System, NullLogger<UpdateProductPriceCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new UpdateProductPriceCommand
            {
                ProductId = product.Id,
                NewPrice = new MoneyDto { Amount = 9.99m, Currency = "USD" },
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);
            refreshed.PopDomainEvents().OfType<ProductPriceChangedDomainEvent>().Should().BeEmpty();
        }
    }
}
