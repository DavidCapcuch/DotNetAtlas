using System.Net;
using System.Net.Http.Json;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Application.Categories.GetCategoryTree;
using Catalog.IntegrationTests.Common;
using FastEndpoints;

namespace Catalog.IntegrationTests.ApiEndpoints.Categories;

[Collection<IntegrationTestCollection>]
public class GetCategoryTreeTests : BaseIntegrationTest
{
    public GetCategoryTreeTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenTreeEmpty_Returns200_WithEmptyNodes()
    {
        // Use raw HttpClient.GetFromJsonAsync rather than FastEndpoints' GETAsync<,,> — the
        // latter serialises a Guid? property as RootCategoryId= (empty value), which the
        // FastEndpoints query-binder converts to Guid.Empty rather than null. The handler
        // then short-circuits the response to an empty tree even when categories exist.
        var response = await HttpClientRegistry.ReadClient
            .GetAsync("/api/v1/catalog/categories/tree", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GetCategoryTreeResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Nodes.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhenTreePopulated_Returns200_WithHierarchy()
    {
        var (_, electronics) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Electronics"));
        await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Laptops", parentCategoryId: electronics.CategoryId));

        var response = await HttpClientRegistry.ReadClient
            .GetAsync("/api/v1/catalog/categories/tree", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GetCategoryTreeResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Nodes.Should().HaveCount(2);
            body.Nodes.Should().Contain(n => n.ParentCategoryId == null && n.Depth == 1);
            body.Nodes.Should().Contain(n => n.ParentCategoryId == electronics.CategoryId && n.Depth == 2);
        }
    }

    [Fact]
    public async Task WhenNoRootFilter_CountsOnlyActiveProductsPerNode()
    {
        // Folded from GetCategoryTreeQueryHandlerTests: ProductCount reflects ACTIVE products only — the
        // discontinued row under Laptops must not inflate it. Seed categories + rows directly for precise
        // control, then read the tree through the endpoint.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var root = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        var laptops = await seeder.SeedCategoryAsync(CatalogFactories.ChildCategory(root, "Laptops"), ct);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(categoryId: laptops.Id, categoryPath: laptops.Path.Value),
            ProductSearchViewRowBuilder.Active(categoryId: laptops.Id, categoryPath: laptops.Path.Value, sku: "ACT-2"),
            ProductSearchViewRowBuilder.Discontinued(categoryId: laptops.Id, categoryPath: laptops.Path.Value));

        var body = await GetTreeAsync();

        using (new AssertionScope())
        {
            body.Nodes.Should().HaveCount(2);
            body.Nodes.Single(n => n.CategoryId == laptops.Id).ProductCount.Should().Be(2);
            body.Nodes.Single(n => n.CategoryId == root.Id).Depth.Should().Be(1);
            body.Nodes.Single(n => n.CategoryId == laptops.Id).Depth.Should().Be(2);
        }
    }

    [Fact]
    public async Task WhenRootFilter_ReturnsOnlySubtree()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var rootA = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Books"), ct);
        var child = await seeder.SeedCategoryAsync(CatalogFactories.ChildCategory(rootA, "Laptops"), ct);

        var body = await GetTreeAsync(rootA.Id);

        body.Nodes.Select(n => n.CategoryId).Should().BeEquivalentTo([rootA.Id, child.Id]);
    }

    [Fact]
    public async Task WhenRootFilterSiblingSharesLeadingSubstring_SiblingIsExcluded()
    {
        // Root "/electronics" must match itself and its descendants, but NOT the sibling
        // "/electronics-toys" whose raw path shares the leading substring.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var electronics = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics Toys"), ct);
        var laptops = await seeder.SeedCategoryAsync(CatalogFactories.ChildCategory(electronics, "Laptops"), ct);

        var body = await GetTreeAsync(electronics.Id);

        body.Nodes.Select(n => n.CategoryId).Should().BeEquivalentTo([electronics.Id, laptops.Id]);
    }

    [Fact]
    public async Task WhenUnknownRoot_ReturnsEmpty()
    {
        // A present category must be filtered out when an unknown subtree root is requested.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);

        var body = await GetTreeAsync(Guid.CreateVersion7());

        body.Nodes.Should().BeEmpty();
    }

    private async Task<GetCategoryTreeResponse> GetTreeAsync(Guid? rootCategoryId = null)
    {
        var url = rootCategoryId is { } id
            ? $"/api/v1/catalog/categories/tree?rootCategoryId={id}"
            : "/api/v1/catalog/categories/tree";
        var response = await HttpClientRegistry.ReadClient.GetAsync(url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GetCategoryTreeResponse>(
            TestContext.Current.CancellationToken))!;
    }
}
