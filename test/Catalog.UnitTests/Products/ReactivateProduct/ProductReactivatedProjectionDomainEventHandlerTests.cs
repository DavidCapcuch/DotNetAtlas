using Catalog.Application.Products.ReactivateProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Products.ReactivateProduct;

public class ProductReactivatedProjectionDomainEventHandlerTests
{
    [Fact]
    public async Task Given_DiscontinuedRow_Then_MarksActiveAndSellable()
    {
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Discontinued();
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductReactivatedProjectionDomainEventHandler(
            db, NullLogger<ProductReactivatedProjectionDomainEventHandler>.Instance);

        var occurredOn = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        await handler.Handle(
            new ProductReactivatedDomainEvent
            {
                ProductId = row.ProductId,
                OccurredOnUtc = occurredOn,
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var refreshed = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == row.ProductId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            refreshed.Status.Should().Be("Active");
            refreshed.IsSellable.Should().BeTrue();
            refreshed.LastUpdatedAtUtc.Should().Be(occurredOn);
        }
    }
}
