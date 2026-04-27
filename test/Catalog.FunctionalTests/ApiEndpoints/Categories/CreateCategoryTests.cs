using System.Net;
using Catalog.API.Endpoints.Categories.CreateCategory;
using Catalog.FunctionalTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.Core;

namespace Catalog.FunctionalTests.ApiEndpoints.Categories;

[Collection<FunctionalTestCollection>]
public class CreateCategoryTests : BaseApiTest
{
    public CreateCategoryTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenRootCategory_Returns201_AndOutboxRow()
    {
        var request = CatalogTestData.ValidCreateCategoryRequest();

        var (response, body) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            body.CategoryId.Should().NotBeEmpty();

            var rows = await DbContext.Set<OutboxMessage>()
                .Where(m => m.KafkaKey == body.CategoryId.ToString()
                            && m.Type!.Contains("CategoryCreated"))
                .CountAsync(TestContext.Current.CancellationToken);
            rows.Should().Be(1);
        }
    }

    [Fact]
    public async Task WhenChildCategory_Returns201_WithCorrectPath()
    {
        var (_, parent) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Electronics"));

        var (response, child) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Laptops", parentCategoryId: parent.CategoryId));

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var path = await DbContext.Categories.AsNoTracking()
                .Where(c => c.Id == child.CategoryId)
                .Select(c => c.Path.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            path.Should().Contain("electronics");
            path.Should().Contain("laptops");
        }
    }

    [Fact]
    public async Task WhenParentMissing_Returns404()
    {
        var request = CatalogTestData.ValidCreateCategoryRequest(parentCategoryId: Guid.CreateVersion7());

        var (response, problemDetails) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, ProblemDetails>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Category.ParentNotFound");
        }
    }
}
