using Catalog.Application.Categories.ReparentCategory;
using Catalog.Domain.Categories.Events;
using Catalog.Domain.Categories.ValueObjects;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Categories.ReparentCategory;

public class CategoryReparentedProjectionDomainEventHandlerTests
{
    [Fact]
    public async Task Handle_IsNoOp_InM3_UntilDescendantCascadeShips()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active(categoryPath: "/old");
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CategoryReparentedProjectionDomainEventHandler(
            NullLogger<CategoryReparentedProjectionDomainEventHandler>.Instance);

        // Act
        await handler.Handle(
            new CategoryReparentedDomainEvent
            {
                OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
                CategoryId = Guid.CreateVersion7(),
                OldParentId = null,
                NewParentId = Guid.CreateVersion7(),
                OldPath = CategoryPath.Create("/old").Value,
                NewPath = CategoryPath.Create("/new").Value,
            },
            TestContext.Current.CancellationToken);

        // Assert
        var refreshed = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == row.ProductId, TestContext.Current.CancellationToken);
        refreshed.CategoryPath.Should().Be("/old");
    }
}
