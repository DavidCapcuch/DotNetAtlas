using Catalog.Application.Categories.Common.Services;
using Catalog.Application.Categories.ReparentCategory;
using Catalog.Domain.Categories.Events;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Catalog.UnitTests.Categories.ReparentCategory;

public class ReparentCategoryCommandHandlerTests
{
    private static (ICategoryAncestryService Ancestry, ICategoryPathService Path) Services(bool wouldCycle = false)
    {
        var ancestry = Substitute.For<ICategoryAncestryService>();
        ancestry.WouldCreateCycleAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(wouldCycle));

        var path = Substitute.For<ICategoryPathService>();
        path.RewriteDescendantPathsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        return (ancestry, path);
    }

    [Fact]
    public async Task Given_ChildCategory_When_ReparentToDifferentParent_Then_Succeeds()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root1 = CatalogFactories.RootCategory("Electronics");
        var root2 = CatalogFactories.RootCategory("Books");
        var child = CatalogFactories.ChildCategory(root1, "Laptops");
        db.Categories.AddRange(root1, root2, child);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (ancestry, pathService) = Services();
        var handler = new ReparentCategoryCommandHandler(
            db, ancestry, pathService, NullLogger<ReparentCategoryCommandHandler>.Instance);

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
            await pathService.Received(1).RewriteDescendantPathsAsync(
                "/electronics/laptops",
                "/books/laptops",
                child.Id,
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Given_MissingCategory_Then_FailsNotFound()
    {
        await using var db = FakeCatalogDbContext.Create();
        var (ancestry, pathService) = Services();
        var handler = new ReparentCategoryCommandHandler(
            db, ancestry, pathService, NullLogger<ReparentCategoryCommandHandler>.Instance);

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

        var (ancestry, pathService) = Services();
        var handler = new ReparentCategoryCommandHandler(
            db, ancestry, pathService, NullLogger<ReparentCategoryCommandHandler>.Instance);

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
    public async Task Given_SelfParent_Then_AggregateGuardFiresWithCannotParentToSelf()
    {
        // The validator normally rejects self-parent BEFORE the handler runs, and the
        // ancestry service short-circuits to true when categoryId == newParentId.
        // This test pins the inner-most defensive branch in `Category.Reparent`: even if
        // both upstream guards were bypassed, the aggregate itself surfaces
        // `Category.CannotParentToSelf`. We force that branch by mocking ancestry to
        // return false (skipping the cycle short-circuit) and bypassing the validator
        // (the handler is invoked directly).
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory();
        db.Categories.Add(root);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (ancestry, pathService) = Services(wouldCycle: false);
        var handler = new ReparentCategoryCommandHandler(
            db, ancestry, pathService, NullLogger<ReparentCategoryCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReparentCategoryCommand
            {
                CategoryId = root.Id,
                NewParentCategoryId = root.Id,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle()
                .Which.Should().BeAssignableTo<Platform.SharedKernel.Errors.ValidationError>()
                .Which.ErrorCode.Should().Be("Category.CannotParentToSelf");
            await pathService.DidNotReceive().RewriteDescendantPathsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Given_AncestryServiceDetectsCycle_Then_FailsReparentCreatesCycleAndDoesNotMutate()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory("Electronics");
        var leaf = CatalogFactories.ChildCategory(root, "Laptops");
        db.Categories.AddRange(root, leaf);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (ancestry, pathService) = Services(wouldCycle: true);
        var handler = new ReparentCategoryCommandHandler(
            db, ancestry, pathService, NullLogger<ReparentCategoryCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReparentCategoryCommand
            {
                CategoryId = root.Id,
                NewParentCategoryId = leaf.Id,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle()
                .Which.Should().BeAssignableTo<Platform.SharedKernel.Errors.ValidationError>()
                .Which.ErrorCode.Should().Be("Category.ReparentCreatesCycle");
            await pathService.DidNotReceive().RewriteDescendantPathsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>());
            // Aggregate path unchanged.
            var refreshed = await db.Categories.FirstAsync(
                c => c.Id == root.Id, TestContext.Current.CancellationToken);
            refreshed.Path.Value.Should().Be("/electronics");
        }
    }
}
