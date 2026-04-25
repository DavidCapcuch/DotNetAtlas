using Catalog.Application.Categories.Common.Services;
using Catalog.UnitTests.Common;

namespace Catalog.UnitTests.Categories.Common.Services;

public class CategoryAncestryServiceTests
{
    [Fact]
    public async Task Given_NewParentEqualsCategory_Returns_True()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory();
        db.Categories.Add(root);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new CategoryAncestryService(db);

        var result = await sut.WouldCreateCycleAsync(
            categoryId: root.Id,
            newParentCategoryId: root.Id,
            TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Given_NewParentIsDirectChild_Returns_True()
    {
        // Reparenting /electronics under /electronics/laptops would create a cycle.
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory("Electronics");
        var child = CatalogFactories.ChildCategory(root, "Laptops");
        db.Categories.AddRange(root, child);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new CategoryAncestryService(db);

        var result = await sut.WouldCreateCycleAsync(
            categoryId: root.Id,
            newParentCategoryId: child.Id,
            TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Given_NewParentIsDeepDescendant_Returns_True()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory("Electronics");
        var mid = CatalogFactories.ChildCategory(root, "Computers");
        var leaf = CatalogFactories.ChildCategory(mid, "Laptops");
        db.Categories.AddRange(root, mid, leaf);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new CategoryAncestryService(db);

        var result = await sut.WouldCreateCycleAsync(
            categoryId: root.Id,
            newParentCategoryId: leaf.Id,
            TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Given_NewParentIsSibling_Returns_False()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory("Electronics");
        var sibling1 = CatalogFactories.ChildCategory(root, "Laptops");
        var sibling2 = CatalogFactories.ChildCategory(root, "Phones");
        db.Categories.AddRange(root, sibling1, sibling2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new CategoryAncestryService(db);

        var result = await sut.WouldCreateCycleAsync(
            categoryId: sibling1.Id,
            newParentCategoryId: sibling2.Id,
            TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Given_NewParentIsAncestor_Returns_False()
    {
        // Moving a deep node under one of its ancestors is fine — no cycle.
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory("Electronics");
        var mid = CatalogFactories.ChildCategory(root, "Computers");
        var leaf = CatalogFactories.ChildCategory(mid, "Laptops");
        db.Categories.AddRange(root, mid, leaf);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new CategoryAncestryService(db);

        var result = await sut.WouldCreateCycleAsync(
            categoryId: leaf.Id,
            newParentCategoryId: root.Id,
            TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Given_NewParentSharesPathPrefixButDifferentSegment_Returns_False()
    {
        // /electronics is NOT a descendant of /electronics-toys (segment-bounded match).
        // Regression of M3's H2 segment-bounded prefix fix.
        await using var db = FakeCatalogDbContext.Create();
        var elec = CatalogFactories.RootCategory("Electronics");
        var toys = CatalogFactories.RootCategory("Electronics Toys");
        db.Categories.AddRange(elec, toys);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new CategoryAncestryService(db);

        var result = await sut.WouldCreateCycleAsync(
            categoryId: elec.Id,
            newParentCategoryId: toys.Id,
            TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Given_MissingCategoryRow_Returns_False()
    {
        // The handler short-circuits NotFound before calling the service; defensively the
        // service treats the missing-row case as "no cycle" so the handler's branching wins.
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory();
        db.Categories.Add(root);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new CategoryAncestryService(db);

        var result = await sut.WouldCreateCycleAsync(
            categoryId: Guid.CreateVersion7(),
            newParentCategoryId: root.Id,
            TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }
}
