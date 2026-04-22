using Catalog.Application.Categories.ReparentCategory;
using Catalog.Domain.Categories.Events;
using Catalog.Domain.Categories.ValueObjects;
using Catalog.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Categories.ReparentCategory;

public class CategoryReparentedProjectionHandlerTests
{
    [Fact]
    public async Task Handle_IsNoOp_InM3_UntilDescendantCascadeShips()
    {
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active(categoryPath: "/old");
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CategoryReparentedProjectionHandler(
            NullLogger<CategoryReparentedProjectionHandler>.Instance);

        await handler.Handle(
            new CategoryReparentedDomainEvent
            {
                CategoryId = Guid.CreateVersion7(),
                OldParentId = null,
                NewParentId = Guid.CreateVersion7(),
                OldPath = CategoryPath.Create("/old").Value,
                NewPath = CategoryPath.Create("/new").Value,
            },
            TestContext.Current.CancellationToken);

        var refreshed = await db.ProductSearchView.FirstAsync(
            r => r.ProductId == row.ProductId, TestContext.Current.CancellationToken);
        refreshed.CategoryPath.Should().Be("/old");
    }
}
