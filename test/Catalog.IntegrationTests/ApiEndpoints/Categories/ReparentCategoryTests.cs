using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Categories.ReparentCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.IntegrationTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Catalog.IntegrationTests.ApiEndpoints.Categories;

[Collection<IntegrationTestCollection>]
public class ReparentCategoryTests : BaseIntegrationTest
{
    public ReparentCategoryTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenValidReparent_Returns204_AndPathRecomputed()
    {
        // electronics > laptops; reparent laptops under accessories.
        var (_, electronics) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Electronics"));
        var (_, laptops) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Laptops", parentCategoryId: electronics.CategoryId));
        var (_, accessories) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Accessories"));

        var response = await HttpClientRegistry.WriteClient
            .PUTAsync<ReparentCategoryEndpoint, ReparentCategoryRequest>(new ReparentCategoryRequest
            {
                Id = laptops.CategoryId,
                NewParentCategoryId = accessories.CategoryId,
            });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var newPath = await DbContext.Categories.AsNoTracking()
                .Where(c => c.Id == laptops.CategoryId)
                .Select(c => c.Path.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            newPath.Should().Contain("accessories");
            newPath.Should().NotContain("electronics");
        }
    }

    [Fact]
    public async Task WhenReparentToSelf_Returns422()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());

        var (response, problemDetails) = await HttpClientRegistry.WriteClient
            .PUTAsync<ReparentCategoryEndpoint, ReparentCategoryRequest, ProblemDetails>(
                new ReparentCategoryRequest
                {
                    Id = cat.CategoryId,
                    NewParentCategoryId = cat.CategoryId,
                });

        using (new AssertionScope())
        {
            // FluentValidation maps to 400 by default in FastEndpoints; the application-side
            // domain rule maps to 422 via the ResultsExtensions status-code map. Both are
            // legitimate gates for "cannot parent to self".
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.BadRequest,
                HttpStatusCode.UnprocessableEntity);
            problemDetails.Errors.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task WhenCategoryMissing_Returns404()
    {
        var (_, target) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());

        var (response, _) = await HttpClientRegistry.WriteClient
            .PUTAsync<ReparentCategoryEndpoint, ReparentCategoryRequest, ProblemDetails>(
                new ReparentCategoryRequest
                {
                    Id = Guid.CreateVersion7(),
                    NewParentCategoryId = target.CategoryId,
                });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task WhenReparentSubtree_CascadesDescendantPathsAndProjectionRows()
    {
        // Folded from ReparentCategoryIntegrationTests (#193 / CAT-RV-H07 / #175): reparenting a
        // mid-tree node must rewrite every descendant Category.Path AND the product_search_view
        // CategoryPath + CategoryBreadcrumb in one bulk SQL update, leaving no stale rows behind.
        // Build electronics > computers > laptops with a product under laptops, then move computers
        // under a fresh two-word root so the breadcrumb humanisation (slug → title-case) is exercised.
        var ct = TestContext.Current.CancellationToken;

        var (_, electronics) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Electronics"));
        var (_, computers) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Computers", parentCategoryId: electronics.CategoryId));
        var (_, laptops) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Laptops", parentCategoryId: computers.CategoryId));
        var (_, betaRoot) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Beta Root"));
        var (_, product) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(laptops.CategoryId));

        // Act — reparent /electronics/computers under /beta-root.
        var response = await HttpClientRegistry.WriteClient
            .PUTAsync<ReparentCategoryEndpoint, ReparentCategoryRequest>(new ReparentCategoryRequest
            {
                Id = computers.CategoryId,
                NewParentCategoryId = betaRoot.CategoryId,
            });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var computersPath = await DbContext.Categories.AsNoTracking()
                .Where(c => c.Id == computers.CategoryId).Select(c => c.Path.Value).SingleAsync(ct);
            var laptopsPath = await DbContext.Categories.AsNoTracking()
                .Where(c => c.Id == laptops.CategoryId).Select(c => c.Path.Value).SingleAsync(ct);
            computersPath.Should().Be("/beta-root/computers");
            laptopsPath.Should().Be("/beta-root/computers/laptops");

            var projection = await DbContext.ProductSearchView.AsNoTracking()
                .FirstAsync(r => r.ProductId == product.ProductId, ct);
            projection.CategoryPath.Should().Be("/beta-root/computers/laptops");
            // CategoryBreadcrumb must cascade alongside CategoryPath: the slug "beta-root" humanises to
            // "Beta Root" (split on '-', title-case each segment), and the old "Electronics" prefix must
            // not survive on descendants.
            projection.CategoryBreadcrumb.Should()
                .StartWith("Beta Root")
                .And.EndWith("> Computers > Laptops")
                .And.NotContain("Electronics");

            // Old subtree must not survive anywhere.
            var stale = await DbContext.Categories.AsNoTracking()
                .Where(c => c.Path.Value.StartsWith("/electronics/computers"))
                .ToListAsync(ct);
            stale.Should().BeEmpty("ExecuteUpdate should have rewritten every descendant of the moved subtree");
        }
    }
}
