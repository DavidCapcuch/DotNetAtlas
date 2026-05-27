using Catalog.Application.Products.DescribeProduct;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Products.DescribeProduct;

public class ProductDescribedProjectionDomainEventHandlerTests
{
    [Fact]
    public async Task Given_ExistingRow_Then_UpdatesDescription()
    {
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active();
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductDescribedProjectionDomainEventHandler(
            db, NullLogger<ProductDescribedProjectionDomainEventHandler>.Instance);

        var occurredOn = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        await handler.Handle(
            new ProductDescribedDomainEvent
            {
                ProductId = row.ProductId,
                NewDescription = ProductDescription.Create("new desc").Value,
                OccurredOnUtc = occurredOn,
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var refreshed = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == row.ProductId, TestContext.Current.CancellationToken);
        refreshed.Description.Should().Be("new desc");
        refreshed.LastUpdatedAtUtc.Should().Be(occurredOn);
    }
}
