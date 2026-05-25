using Catalog.Application.Categories.CreateCategory;
using Catalog.Application.Categories.ReparentCategory;
using Catalog.Application.Products.CreateProduct;
using Catalog.Infrastructure.Persistence.Database;
using Catalog.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Catalog.IntegrationTests.Categories;

/// <summary>
/// Integration coverage for the reparent path-cascade per CAT-TST / wave-1 follow-up #193.
/// Unit tests use NSubstitute around <c>ICategoryPathService</c> because EF Core InMemory
/// has no implementation for <c>ExecuteUpdateAsync</c>. This test exercises the real bulk
/// SQL update against a Postgres Testcontainer so the descendant <c>Category.Path</c>
/// rewrite and the <c>product_search_view.CategoryPath</c> rewrite are both verified
/// against the actual SQL.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class ReparentCategoryIntegrationTests : BaseIntegrationTest
{
    public ReparentCategoryIntegrationTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task ReparentCategory_RewritesDescendantPathsInDatabase_And_ProjectionViewRows()
    {
        // Arrange — build a 3-level tree under a fresh root pair to avoid cross-test bleed:
        //   /alpha-{run}, /alpha-{run}/computers, /alpha-{run}/computers/laptops
        //   /beta-{run}  (target root for the reparent)
        // Stick a product under the deepest category so we can assert the path-cascade
        // touched product_search_view too.
        var run = Guid.CreateVersion7().ToString("N")[..8];
        Guid alphaId;
        Guid betaId;
        Guid computersId;
        Guid laptopsId;
        Guid productId;
        using (var scope = Fixture.CreateScope())
        {
            var categoryHandler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<CreateCategoryCommand, Guid>>();
            alphaId = (await categoryHandler.HandleAsync(
                new CreateCategoryCommand { Name = $"Alpha-{run}" },
                TestContext.Current.CancellationToken)).Value;
            betaId = (await categoryHandler.HandleAsync(
                new CreateCategoryCommand { Name = $"Beta-{run}" },
                TestContext.Current.CancellationToken)).Value;
            computersId = (await categoryHandler.HandleAsync(
                new CreateCategoryCommand { Name = "Computers", ParentCategoryId = alphaId },
                TestContext.Current.CancellationToken)).Value;
            laptopsId = (await categoryHandler.HandleAsync(
                new CreateCategoryCommand { Name = "Laptops", ParentCategoryId = computersId },
                TestContext.Current.CancellationToken)).Value;

            var productHandler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<CreateProductCommand, Guid>>();
            var sku = $"REP-{run}".ToUpperInvariant();
            productId = (await productHandler.HandleAsync(
                new CreateProductCommand
                {
                    Sku = sku,
                    Name = "Reparent Widget",
                    Description = "Round-trips through reparent cascade.",
                    CategoryId = laptopsId,
                    Brand = "TestBrand",
                    Price = new MoneyDto { Amount = 1m, Currency = "USD" },
                    Images = [],
                },
                TestContext.Current.CancellationToken)).Value;
        }

        // Act — reparent /alpha-{run}/computers under /beta-{run}. Per CategoryPathService,
        // computers + every descendant should have its path rewritten in a single SQL transaction.
        using (var scope = Fixture.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReparentCategoryCommand>>();
            var result = await handler.HandleAsync(
                new ReparentCategoryCommand
                {
                    CategoryId = computersId,
                    NewParentCategoryId = betaId,
                },
                TestContext.Current.CancellationToken);
            result.Should().BeSuccess();
        }

        // Assert against a fresh scope so we read what was committed, not what the prior
        // scope's change-tracker held (the M3.5 fix clears the tracker after the cascade).
        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // Guid.ToString("N") already emits lowercase hex; concatenating into the slug below
        // is safe.
        var alphaPath = $"/alpha-{run}";
        var betaPath = $"/beta-{run}";
        var computers = await db.Categories.AsNoTracking()
            .FirstAsync(c => c.Id == computersId, TestContext.Current.CancellationToken);
        var laptops = await db.Categories.AsNoTracking()
            .FirstAsync(c => c.Id == laptopsId, TestContext.Current.CancellationToken);
        var projection = await db.ProductSearchView.AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            computers.Path.Value.Should().Be($"{betaPath}/computers");
            laptops.Path.Value.Should().Be($"{betaPath}/computers/laptops");
            projection.CategoryPath.Should().Be($"{betaPath}/computers/laptops");

            // CAT-RV-H07 / #175: CategoryBreadcrumb must cascade alongside CategoryPath.
            // The slug "alpha-{run}" / "beta-{run}" humanises into "Alpha {Run} > Computers
            // > Laptops" via CategoryBreadcrumbBuilder (split on '-', title-case each token,
            // join with space; then segments joined with " > ").
            projection.CategoryBreadcrumb.Should()
                .StartWith($"Beta {run.ToUpperInvariant()[0]}{run[1..]}")
                .And.EndWith("> Computers > Laptops")
                .And.NotContain("Alpha", "the old taxonomy prefix must not survive on descendants");

            // Old subtree must not survive anywhere.
            var stale = await db.Categories.AsNoTracking()
                .Where(c => c.Path.Value.StartsWith($"{alphaPath}/computers"))
                .ToListAsync(TestContext.Current.CancellationToken);
            stale.Should().BeEmpty(
                "ExecuteUpdate should have rewritten every descendant of the moved subtree");
        }
    }
}
