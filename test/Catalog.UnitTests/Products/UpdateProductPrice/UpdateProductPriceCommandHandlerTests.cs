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
    public async Task Handle_ActiveProductWithNewAmountDiffering_UpdatesAmountKeepsCurrencyAndRaisesEvent()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        // Seeded in a NON-default currency (EUR ≠ the factory default USD) so this test kills a
        // handler that hardcodes a default currency instead of reusing the product's: such a mutant
        // would build EUR-amount → USD Money, which Product.UpdatePrice rejects (ADR-0002 currency
        // guard) → the reprice fails and BeSuccess() below flips red.
        var product = CatalogFactories.ActiveProduct(category, currency: "EUR");
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var originalCurrency = product.Price.Currency;

        var clock = new FakeTimeProvider(FixedUtc);
        var handler = new UpdateProductPriceCommandHandler(
            db, clock, NullLogger<UpdateProductPriceCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new UpdateProductPriceCommand
            {
                ProductId = product.Id,
                NewAmount = 42m,
            },
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);
            refreshed.Price.Amount.Should().Be(42m);
            refreshed.Price.Currency.Should().Be(originalCurrency);
            var raised = refreshed.PopDomainEvents().OfType<ProductPriceChangedDomainEvent>().Single();
            raised.NewPrice.Amount.Should().Be(42m);
            raised.NewPrice.Currency.Should().Be(originalCurrency);
            raised.OccurredOnUtc.Should().Be(FixedUtc);
        }
    }

    [Fact]
    public async Task Handle_MissingProduct_FailsWithNotFound()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var handler = new UpdateProductPriceCommandHandler(
            db, TimeProvider.System, NullLogger<UpdateProductPriceCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new UpdateProductPriceCommand
            {
                ProductId = Guid.CreateVersion7(),
                NewAmount = 1m,
            },
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e => ((DomainError)e).ErrorCode == "Product.NotFound");
    }

    [Fact]
    public async Task Handle_DiscontinuedProduct_FailsWithCannotReprice()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DiscontinuedProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new UpdateProductPriceCommandHandler(
            db, TimeProvider.System, NullLogger<UpdateProductPriceCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new UpdateProductPriceCommand
            {
                ProductId = product.Id,
                NewAmount = 42m,
            },
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("discontinued", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_IdenticalPrice_SucceedsWithoutRaisingEvent()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new UpdateProductPriceCommandHandler(
            db, TimeProvider.System, NullLogger<UpdateProductPriceCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            new UpdateProductPriceCommand
            {
                ProductId = product.Id,
                NewAmount = 9.99m,
            },
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Products.FirstAsync(
                p => p.Id == product.Id, TestContext.Current.CancellationToken);
            refreshed.PopDomainEvents().OfType<ProductPriceChangedDomainEvent>().Should().BeEmpty();
        }
    }
}
