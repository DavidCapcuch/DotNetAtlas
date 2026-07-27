using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Application.Categories.GetProductsByCategory;
using Catalog.Application.Common.Contracts;
using Catalog.IntegrationTests.Common;
using FastEndpoints;

namespace Catalog.IntegrationTests.ApiEndpoints.Categories;

[Collection<IntegrationTestCollection>]
public class GetProductsByCategoryTests : BaseIntegrationTest
{
    public GetProductsByCategoryTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    // Post-#177: products are Active on create, so the Status == Active filter in
    // GetProductsByCategoryQueryHandler surfaces them directly without an Activate step.
    [Fact]
    public async Task WhenCategoryHasProducts_Returns200_WithItems()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Books"));
        var (_, product) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(cat.CategoryId));

        // Use raw HttpClient — FastEndpoints' GETAsync<,,> serialises null bool? as
        // includeDescendants= which the binder converts to default false; that is the
        // intended behaviour but the empty value also pollutes other Guid? params elsewhere
        // (see GetCategoryTreeTests for the equivalent). Use the explicit URL here.
        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/categories/{cat.CategoryId}/products",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GetProductsByCategoryResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().Contain(i => i.ProductId == product.ProductId);
        }
    }

    [Fact]
    public async Task WhenIncludeDescendantsTrue_ReturnsProductsFromChildCategories()
    {
        var (_, electronics) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Electronics"));
        var (_, laptops) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Laptops", parentCategoryId: electronics.CategoryId));
        var (_, laptop) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(laptops.CategoryId));

        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/categories/{electronics.CategoryId}/products?includeDescendants=true",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GetProductsByCategoryResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().Contain(i => i.ProductId == laptop.ProductId);
        }
    }

    [Fact]
    public async Task WhenIncludeDescendantsFalse_MatchesCategoryIdOnly()
    {
        // Folded from GetProductsByCategoryQueryHandlerTests: with descendants off, only rows whose
        // CategoryId equals the queried category match — a product in another category is excluded.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var category = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "MATCH", categoryId: category.Id, categoryPath: category.Path.Value),
            ProductSearchViewRowBuilder.Active(sku: "OTHER"));

        var body = await GetByCategoryAsync(category.Id, includeDescendants: false);

        body.Items.Should().ContainSingle().Which.Sku.Should().Be("MATCH");
    }

    [Fact]
    public async Task WhenIncludeDescendantsTrueSiblingSharesLeadingSubstring_SiblingIsExcluded()
    {
        // Root "/electronics" must match itself and its descendants, but NOT the sibling
        // "/electronics-toys" whose raw path shares the leading substring.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var root = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "EXACT", categoryId: root.Id, categoryPath: root.Path.Value),
            ProductSearchViewRowBuilder.Active(sku: "CHILD", categoryPath: root.Path.Value + "/laptops"),
            ProductSearchViewRowBuilder.Active(sku: "SIBLING", categoryPath: root.Path.Value + "-toys"));

        var body = await GetByCategoryAsync(root.Id, includeDescendants: true);

        body.Items.Select(i => i.Sku).Should().BeEquivalentTo(["EXACT", "CHILD"]);
    }

    [Fact]
    public async Task WhenUnknownCategoryWithDescendants_ReturnsEmptyPage()
    {
        var body = await GetByCategoryAsync(Guid.CreateVersion7(), includeDescendants: true);

        body.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenCategoryHasProducts_PayloadMatchesPublishedWireContract()
    {
        // The wire payload is this endpoint's published contract. Asserted over raw JSON so the
        // guard holds independently of which CLR type the endpoint returns — a dropped, renamed,
        // re-nested or crossed member fails here.
        // Paging is requested off its defaults (page 2 of 1) so the echoed pageNumber/pageSize
        // cannot pass by coincidence, and total (2) stays distinguishable from the item count (1).
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var category = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(
                sku: "AAA-000",
                name: "AAA Filler",
                categoryId: category.Id),
            ProductSearchViewRowBuilder
                .Active(
                    sku: "WIRE-001",
                    name: "Wire Widget",
                    categoryId: category.Id,
                    categoryPath: "/electronics/laptops",
                    amount: 19.95m,
                    currency: "EUR")
                .WithImages(
                    new ImageReferenceDto { Url = "https://cdn.test/secondary.png", AltText = "b", DisplayOrder = 2 },
                    new ImageReferenceDto { Url = "https://cdn.test/primary.png", AltText = "a", DisplayOrder = 1 }));

        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/categories/{category.Id}/products?includeDescendants=false&page=2&limit=1",
            ct);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var root = payload.RootElement;
        var item = root.GetProperty("items").EnumerateArray().Single();
        var price = item.GetProperty("price");

        using (new AssertionScope())
        {
            root.EnumerateObject().Select(p => p.Name)
                .Should().BeEquivalentTo("total", "pageNumber", "pageSize", "items");
            item.EnumerateObject().Select(p => p.Name)
                .Should().BeEquivalentTo(
                    "productId", "sku", "name", "categoryBreadcrumb", "brandName", "price", "status",
                    "primaryImageUrl");
            price.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("amount", "currency");

            root.GetProperty("total").GetInt32().Should().Be(2);
            root.GetProperty("pageNumber").GetInt32().Should().Be(2);
            root.GetProperty("pageSize").GetInt32().Should().Be(1);
            item.GetProperty("sku").GetString().Should().Be("WIRE-001");
            item.GetProperty("name").GetString().Should().Be("Wire Widget");
            item.GetProperty("status").GetString().Should().Be("Active");
            item.GetProperty("brandName").GetString().Should().Be("Acme");
            item.GetProperty("categoryBreadcrumb").GetString().Should().Be("electronics > laptops");
            price.GetProperty("amount").GetDecimal().Should().Be(19.95m);
            price.GetProperty("currency").GetString().Should().Be("EUR");

            // Primary image is the lowest DisplayOrder, not the first in source order.
            item.GetProperty("primaryImageUrl").GetString().Should().Be("https://cdn.test/primary.png");
        }
    }

    private async Task<GetProductsByCategoryResponse> GetByCategoryAsync(Guid categoryId, bool includeDescendants)
    {
        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/categories/{categoryId}/products?includeDescendants={(includeDescendants ? "true" : "false")}",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GetProductsByCategoryResponse>(
            TestContext.Current.CancellationToken))!;
    }
}
