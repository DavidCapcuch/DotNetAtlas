using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.GetProductById;
using Catalog.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;

namespace Catalog.IntegrationTests.Products.GetProductById;

[Collection<IntegrationTestCollection>]
public sealed class GetProductByIdQueryHandlerTests : BaseIntegrationTest
{
    public GetProductByIdQueryHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task Given_ExistingRow_When_Querying_Then_ReturnsDetailResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var row = ProductSearchViewRowBuilder.Active()
            .WithImages(new ImageReferenceDto { Url = "https://cdn.example.com/a.jpg", AltText = "a", DisplayOrder = 0 });
        await seeder.SeedRowsAsync(ct, row);

        var handler = new GetProductByIdQueryHandler(CatalogDbContext);

        var result = await handler.HandleAsync(new GetProductByIdQuery { ProductId = row.ProductId }, ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.ProductId.Should().Be(row.ProductId);
            result.Value.Sku.Should().Be(row.Sku);
            result.Value.Price.Amount.Should().Be(row.PriceAmount);
            result.Value.Images.Should().ContainSingle().Which.AltText.Should().Be("a");
        }
    }

    [Fact]
    public async Task Given_MissingRow_When_Querying_Then_FailsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new GetProductByIdQueryHandler(CatalogDbContext);

        var result = await handler.HandleAsync(new GetProductByIdQuery { ProductId = Guid.CreateVersion7() }, ct);

        result.Should().BeFailure();
    }
}
