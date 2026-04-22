using Catalog.Application.Categories.ReparentCategory;
using Catalog.Domain.Categories.Events;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Categories.ReparentCategory;

public class ReparentCategoryCommandHandlerTests
{
    [Fact]
    public async Task Given_ChildCategory_When_ReparentToDifferentParent_Then_Succeeds()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root1 = CatalogFactories.RootCategory("Electronics");
        var root2 = CatalogFactories.RootCategory("Books");
        var child = CatalogFactories.ChildCategory(root1, "Laptops");
        db.Categories.AddRange(root1, root2, child);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ReparentCategoryCommandHandler(
            db, NullLogger<ReparentCategoryCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReparentCategoryCommand
            {
                CategoryId = child.Id,
                NewParentCategoryId = root2.Id,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var refreshed = await db.Categories.FirstAsync(
                c => c.Id == child.Id, TestContext.Current.CancellationToken);
            refreshed.ParentCategoryId.Should().Be(root2.Id);
            refreshed.Path.Value.Should().Be("/books/laptops");
            refreshed.PopDomainEvents().OfType<CategoryReparentedDomainEvent>()
                .Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Given_MissingCategory_Then_FailsNotFound()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new ReparentCategoryCommandHandler(
            db, NullLogger<ReparentCategoryCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReparentCategoryCommand
            {
                CategoryId = Guid.CreateVersion7(),
                NewParentCategoryId = null,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Given_MissingNewParent_Then_FailsNotFound()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory();
        db.Categories.Add(root);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ReparentCategoryCommandHandler(
            db, NullLogger<ReparentCategoryCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReparentCategoryCommand
            {
                CategoryId = root.Id,
                NewParentCategoryId = Guid.CreateVersion7(),
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Given_SelfParent_Then_FailsCannotParentToSelf()
    {
        // Validator normally guards this, but the handler's call to Category.Reparent
        // also rejects it defensively.
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory();
        db.Categories.Add(root);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ReparentCategoryCommandHandler(
            db, NullLogger<ReparentCategoryCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReparentCategoryCommand
            {
                CategoryId = root.Id,
                NewParentCategoryId = root.Id,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }
}
