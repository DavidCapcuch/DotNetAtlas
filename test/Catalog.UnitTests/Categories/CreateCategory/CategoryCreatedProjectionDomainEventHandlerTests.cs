using Catalog.Application.Categories.CreateCategory;
using Catalog.Domain.Categories.Events;
using Catalog.Domain.Categories.ValueObjects;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Categories.CreateCategory;

public class CategoryCreatedProjectionDomainEventHandlerTests
{
    [Fact]
    public async Task Handle_IsNoOp_AndDoesNotChangeProductSearchView()
    {
        await using var db = FakeCatalogDbContext.Create();
        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var before = await db.ProductSearchView.CountAsync(TestContext.Current.CancellationToken);

        var handler = new CategoryCreatedProjectionDomainEventHandler(
            NullLogger<CategoryCreatedProjectionDomainEventHandler>.Instance);

        await handler.Handle(
            new CategoryCreatedDomainEvent
            {
                OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
                CategoryId = Guid.CreateVersion7(),
                Name = "Electronics",
                ParentCategoryId = null,
                Path = CategoryPath.Create("/electronics").Value,
            },
            TestContext.Current.CancellationToken);

        var after = await db.ProductSearchView.CountAsync(TestContext.Current.CancellationToken);
        after.Should().Be(before);
    }
}
