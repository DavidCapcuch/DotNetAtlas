using System.Net;
using System.Text.Json;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Api.Endpoints.Products.GetProductById;
using Catalog.Application.Common.ReadModels;
using Catalog.Application.Products.GetProductById;
using Catalog.IntegrationTests.Common;
using FastEndpoints;

namespace Catalog.IntegrationTests.ApiEndpoints.Products;

[Collection<IntegrationTestCollection>]
public class GetProductByIdTests : BaseIntegrationTest
{
    public GetProductByIdTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenProductExists_Returns200_WithFullDto()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        var createReq = CatalogTestData.ValidCreateProductRequest(cat.CategoryId, name: "Acme Pro");
        var (_, created) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(createReq);

        var (response, body) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductByIdEndpoint, GetProductByIdRequest, GetProductByIdResponse>(
                new GetProductByIdRequest { Id = created.ProductId });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.ProductId.Should().Be(created.ProductId);
            body.Name.Should().Be("Acme Pro");
            body.Sku.Should().Be(createReq.Sku);
            body.Price.Currency.Should().Be(createReq.Price.Currency);
            // Post-#177: CreateProduct lands the aggregate directly in Active (Draft removed
            // from the Catalog lifecycle — the only transition is Active ↔ Discontinued).
            body.Status.Should().Be("Active");
            // Closes the round-trip the flattening opened: write model → four scalar columns → wire.
            // Values are distinct per axis, so a transposition at either mapping site fails here.
            body.Dimensions.Should().BeEquivalentTo(createReq.Dimensions);
        }
    }

    [Fact]
    public async Task WhenProductMissing_Returns404()
    {
        var (response, problemDetails) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductByIdEndpoint, GetProductByIdRequest, ProblemDetails>(
                new GetProductByIdRequest { Id = Guid.CreateVersion7() });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Product.NotFound");
        }
    }

    [Fact]
    public async Task WhenProductExists_PayloadMatchesPublishedWireContract()
    {
        // The wire payload is this endpoint's published contract. Basket's anti-corruption layer
        // binds productId/sku/name/price by JSON property name (CatalogProductResponse in
        // Basket.Infrastructure.ExternalServices.Catalog) with no compile-time link to this service,
        // and drops the rest — but the whole payload is published, so every member is asserted here,
        // not just the four with a known consumer today.
        // Asserted over raw JSON so the guard holds independently of which CLR type the endpoint
        // returns — a dropped, renamed, re-nested or crossed member fails here.
        // Every value is distinct per member so a mapping that crosses two of them cannot pass.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var row = ProductSearchViewRowBuilder.Active(
                sku: "WIRE-002",
                name: "Wire Product",
                categoryPath: "/electronics/laptops",
                amount: 21.75m,
                currency: "GBP")
            // Seeded out of DisplayOrder so the assertion below pins stored order — this endpoint
            // returns every image as stored, unlike the listing endpoints which rank by DisplayOrder
            // to pick a single primary image.
            .WithImages(
                new ProductImageDocument { Url = "https://cdn.test/second.png", AltText = "second alt", DisplayOrder = 9 },
                new ProductImageDocument { Url = "https://cdn.test/first.png", AltText = "first alt", DisplayOrder = 3 });
        row.Description = "Wire description";
        row.DimensionsLength = 1.5m;
        row.DimensionsWidth = 2.5m;
        row.DimensionsHeight = 3.5m;
        row.DimensionsUnit = "cm";
        row.CreatedAtUtc = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        row.LastUpdatedAtUtc = new DateTimeOffset(2026, 7, 8, 9, 10, 11, TimeSpan.Zero);
        await seeder.SeedRowsAsync(ct, row);

        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/products/{row.ProductId}",
            ct);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var product = payload.RootElement;
        var price = product.GetProperty("price");
        var dimensions = product.GetProperty("dimensions");
        var images = product.GetProperty("images").EnumerateArray().ToList();
        var image = images[0];

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            product.EnumerateObject().Select(p => p.Name)
                .Should().BeEquivalentTo(
                    "productId", "sku", "name", "description", "categoryId", "categoryPath",
                    "categoryBreadcrumb", "brandName", "price", "status", "dimensions", "images",
                    "createdAtUtc", "lastUpdatedAtUtc");
            price.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("amount", "currency");
            dimensions.EnumerateObject().Select(p => p.Name)
                .Should().BeEquivalentTo("length", "width", "height", "unit");
            image.EnumerateObject().Select(p => p.Name)
                .Should().BeEquivalentTo("url", "altText", "displayOrder");

            product.GetProperty("productId").GetGuid().Should().Be(row.ProductId);
            product.GetProperty("sku").GetString().Should().Be("WIRE-002");
            product.GetProperty("name").GetString().Should().Be("Wire Product");
            product.GetProperty("description").GetString().Should().Be("Wire description");
            product.GetProperty("categoryId").GetGuid().Should().Be(row.CategoryId);
            product.GetProperty("categoryPath").GetString().Should().Be("/electronics/laptops");
            product.GetProperty("categoryBreadcrumb").GetString().Should().Be("electronics > laptops");
            product.GetProperty("brandName").GetString().Should().Be("Acme");
            product.GetProperty("status").GetString().Should().Be("Active");
            product.GetProperty("createdAtUtc").GetDateTimeOffset()
                .Should().Be(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
            product.GetProperty("lastUpdatedAtUtc").GetDateTimeOffset()
                .Should().Be(new DateTimeOffset(2026, 7, 8, 9, 10, 11, TimeSpan.Zero));

            price.GetProperty("amount").GetDecimal().Should().Be(21.75m);
            price.GetProperty("currency").GetString().Should().Be("GBP");

            dimensions.GetProperty("length").GetDecimal().Should().Be(1.5m);
            dimensions.GetProperty("width").GetDecimal().Should().Be(2.5m);
            dimensions.GetProperty("height").GetDecimal().Should().Be(3.5m);
            dimensions.GetProperty("unit").GetString().Should().Be("cm");

            // Stored order, not DisplayOrder order — the seeded pair is deliberately inverted.
            images.Select(i => i.GetProperty("url").GetString())
                .Should().Equal("https://cdn.test/second.png", "https://cdn.test/first.png");
            image.GetProperty("url").GetString().Should().Be("https://cdn.test/second.png");
            image.GetProperty("altText").GetString().Should().Be("second alt");
            image.GetProperty("displayOrder").GetInt32().Should().Be(9);
        }
    }

    [Fact]
    public async Task WhenProductHasNoDimensionsOrImages_PayloadCarriesNullAndEmptyArray()
    {
        // The two optional members of the contract degrade differently, and a consumer binding
        // either one has to know which: the all-or-none dimensions rule reaches the wire as an
        // explicit null (not an omitted or partial member), while absent images are an empty array
        // rather than null.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var row = ProductSearchViewRowBuilder.Active(sku: "WIRE-003");
        await seeder.SeedRowsAsync(ct, row);

        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/products/{row.ProductId}",
            ct);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.RootElement.GetProperty("dimensions").ValueKind.Should().Be(JsonValueKind.Null);
            payload.RootElement.GetProperty("images").ValueKind.Should().Be(JsonValueKind.Array);
            payload.RootElement.GetProperty("images").EnumerateArray().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhenProductHasImages_Returns200_WithImageAndPriceProjectionMapped()
    {
        // Folded from GetProductByIdQueryHandlerTests: the read model persists images as JSON and
        // price as a decimal column, so this pins the query's image deserialization + MoneyDto
        // amount mapping — observables the create-driven test above does not assert. Seed the
        // projection row directly so the image payload is the thing under test, not the create pipeline.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var row = ProductSearchViewRowBuilder.Active(amount: 42.50m)
            .WithImages(new ProductImageDocument { Url = "https://cdn.example.com/a.jpg", AltText = "a", DisplayOrder = 0 });
        await seeder.SeedRowsAsync(ct, row);

        var (response, body) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductByIdEndpoint, GetProductByIdRequest, GetProductByIdResponse>(
                new GetProductByIdRequest { Id = row.ProductId });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.ProductId.Should().Be(row.ProductId);
            body.Price.Amount.Should().Be(42.50m);
            body.Images.Should().ContainSingle().Which.AltText.Should().Be("a");
        }
    }
}
