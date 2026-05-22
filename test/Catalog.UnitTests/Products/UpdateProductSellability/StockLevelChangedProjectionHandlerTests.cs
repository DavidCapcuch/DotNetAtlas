using Catalog.Application.Products.UpdateProductSellability;
using Catalog.Domain.Products.ValueObjects;
using Catalog.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.UnitTests.Products.UpdateProductSellability;

public class StockLevelChangedProjectionHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Given_MissingProductRow_When_Handling_Then_LogsAndReturns()
    {
        // Arrange — no projection row for the productId; handler logs Information and returns.
        await using var db = FakeCatalogDbContext.Create();
        var handler = new StockLevelChangedProjectionHandler(
            db, new FakeTimeProvider(Now), NullLogger<StockLevelChangedProjectionHandler>.Instance);

        // Act
        await handler.HandleAsync(
            Guid.CreateVersion7(), newAvailable: 5, TestContext.Current.CancellationToken);

        // Assert — no exception, nothing persisted.
        db.ProductSearchView.Should().BeEmpty();
    }

    [Fact]
    public async Task Given_ActiveProductWithPositiveStock_When_Handling_Then_SetsIsSellableTrue()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active();
        row.IsSellable = false;
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var time = new FakeTimeProvider(Now);
        var handler = new StockLevelChangedProjectionHandler(
            db, time, NullLogger<StockLevelChangedProjectionHandler>.Instance);

        // Act
        await handler.HandleAsync(row.ProductId, newAvailable: 7, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            row.IsSellable.Should().BeTrue();
            row.LastUpdatedAtUtc.Should().Be(Now);
        }
    }

    [Fact]
    public async Task Given_DiscontinuedProductWithPositiveStock_When_Handling_Then_KeepsIsSellableFalse()
    {
        // Arrange — Status drives sellability: a Discontinued product is never sellable
        // regardless of stock level.
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active();
        row.Status = ProductStatus.Discontinued.Name;
        row.IsSellable = false;
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new StockLevelChangedProjectionHandler(
            db, new FakeTimeProvider(Now), NullLogger<StockLevelChangedProjectionHandler>.Instance);

        // Act
        await handler.HandleAsync(row.ProductId, newAvailable: 100, TestContext.Current.CancellationToken);

        // Assert
        row.IsSellable.Should().BeFalse();
    }

    [Fact]
    public async Task Given_ActiveProductWithZeroStock_When_Handling_Then_SetsIsSellableFalse()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active();
        row.IsSellable = true;
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var time = new FakeTimeProvider(Now);
        var handler = new StockLevelChangedProjectionHandler(
            db, time, NullLogger<StockLevelChangedProjectionHandler>.Instance);

        // Act
        await handler.HandleAsync(row.ProductId, newAvailable: 0, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            row.IsSellable.Should().BeFalse();
            row.LastUpdatedAtUtc.Should().Be(Now);
        }
    }

    [Fact]
    public async Task Given_NoChangeToIsSellable_When_Handling_Then_DoesNotTouchLastUpdatedAt()
    {
        // Arrange — early-exit path: no change to IsSellable means no LastUpdatedAtUtc bump.
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active();
        row.IsSellable = true;
        var originalLastUpdated = row.LastUpdatedAtUtc;
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new StockLevelChangedProjectionHandler(
            db, new FakeTimeProvider(Now), NullLogger<StockLevelChangedProjectionHandler>.Instance);

        // Act
        await handler.HandleAsync(row.ProductId, newAvailable: 12, TestContext.Current.CancellationToken);

        // Assert
        row.LastUpdatedAtUtc.Should().Be(originalLastUpdated);
    }
}
