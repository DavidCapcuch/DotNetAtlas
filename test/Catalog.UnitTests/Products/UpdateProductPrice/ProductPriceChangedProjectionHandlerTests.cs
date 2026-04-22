using Catalog.Application.Products.UpdateProductPrice;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.UnitTests.Products.UpdateProductPrice;

public class ProductPriceChangedProjectionHandlerTests
{
    [Fact]
    public async Task Given_ExistingRow_When_Handling_Then_UpdatesPriceAndTimestamp()
    {
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active(amount: 10m);
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductPriceChangedProjectionHandler(
            db, NullLogger<ProductPriceChangedProjectionHandler>.Instance);

        var now = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);
        var newPrice = Money.Create(42m, "EUR").Value;

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
    public async Task Given_MissingRow_When_Handling_Then_NoOps()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ProductPriceChangedProjectionHandler(
            db, NullLogger<ProductPriceChangedProjectionHandler>.Instance);

        await handler.Handle(
            new ProductPriceChangedDomainEvent
            {
                ProductId = Guid.CreateVersion7(),
                OldPrice = Money.Create(1m, "USD").Value,
                NewPrice = Money.Create(2m, "USD").Value,
            },
            TestContext.Current.CancellationToken);

        (await db.ProductSearchView.CountAsync(TestContext.Current.CancellationToken))
            .Should().Be(0);
    }
}
