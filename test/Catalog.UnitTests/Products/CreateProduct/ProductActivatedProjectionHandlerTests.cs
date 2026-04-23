using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Products.CreateProduct;

public class ProductActivatedProjectionHandlerTests
{
    [Fact]
    public async Task Given_DraftRow_When_Handling_Then_MarksActiveAndSellable()
    {
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active();
        row.Status = "Draft";
        row.IsSellable = false;
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ProductActivatedProjectionHandler(
            db, NullLogger<ProductActivatedProjectionHandler>.Instance);

        var occurredOn = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        await handler.Handle(
            new ProductActivatedDomainEvent
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

    [Fact]
    public async Task Given_MissingRow_When_Handling_Then_NoOps()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ProductActivatedProjectionHandler(
            db, NullLogger<ProductActivatedProjectionHandler>.Instance);

        await handler.Handle(
            new ProductActivatedDomainEvent { ProductId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        (await db.ProductSearchView.CountAsync(TestContext.Current.CancellationToken))
            .Should().Be(0);
    }
}
