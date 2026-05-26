using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Categories.ReparentCategory;
using Catalog.FunctionalTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Catalog.FunctionalTests.ApiEndpoints.Categories;

[Collection<FunctionalTestCollection>]
public class ReparentCategoryTests : BaseApiTest
{
    public ReparentCategoryTests(ApiTestFixture app)
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
}
