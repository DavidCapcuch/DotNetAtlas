using Catalog.Application.Products.UpdateProductPrice;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.UnitTests.Products.UpdateProductPrice;

public class ProductPriceChangedProjectionDomainEventHandlerTests
{
    [Fact]
    public async Task Handle_ExistingRow_UpdatesPriceAndTimestamp()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active(amount: 10m);
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductPriceChangedProjectionDomainEventHandler(
            db, NullLogger<ProductPriceChangedProjectionDomainEventHandler>.Instance);

        var now = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);
        var newPrice = Money.Create(42m, "EUR").Value;

        // Act
        await handler.Handle(
            new ProductPriceChangedDomainEvent
            {
                ProductId = row.ProductId,
                OldPrice = Money.Create(10m, "USD").Value,
                NewPrice = newPrice,
                OccurredOnUtc = now,
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var refreshed = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == row.ProductId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            refreshed.PriceAmount.Should().Be(42m);
            refreshed.PriceCurrency.Should().Be("EUR");
            refreshed.LastUpdatedAtUtc.Should().Be(now);
        }
    }

    [Fact]
    public async Task Handle_MissingRow_NoOps()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ProductPriceChangedProjectionDomainEventHandler(
            db, NullLogger<ProductPriceChangedProjectionDomainEventHandler>.Instance);

        // Act
        await handler.Handle(
            new ProductPriceChangedDomainEvent
            {
                OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
                ProductId = Guid.CreateVersion7(),
                OldPrice = Money.Create(1m, "USD").Value,
                NewPrice = Money.Create(2m, "USD").Value,
            },
            TestContext.Current.CancellationToken);

        // Assert
        (await db.ProductSearchView.CountAsync(TestContext.Current.CancellationToken))
            .Should().Be(0);
    }
}
