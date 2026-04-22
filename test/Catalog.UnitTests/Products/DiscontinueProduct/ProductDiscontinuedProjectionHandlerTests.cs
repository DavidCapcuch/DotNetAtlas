using Catalog.Application.Products.DiscontinueProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Products.DiscontinueProduct;

public class ProductDiscontinuedProjectionHandlerTests
{
    [Fact]
    public async Task Given_ExistingRow_Then_MarksDiscontinuedAndNotSellable()
    {
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active();
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductDiscontinuedProjectionHandler(
            db, NullLogger<ProductDiscontinuedProjectionHandler>.Instance);

        var occurredOn = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        await handler.Handle(
            new ProductDiscontinuedDomainEvent
            {
                ProductId = row.ProductId,
                Reason = "EOL",
                OccurredOnUtc = occurredOn,
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var refreshed = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == row.ProductId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            refreshed.Status.Should().Be("Discontinued");
            refreshed.IsSellable.Should().BeFalse();
            refreshed.LastUpdatedAtUtc.Should().Be(occurredOn);
        }
    }
}
