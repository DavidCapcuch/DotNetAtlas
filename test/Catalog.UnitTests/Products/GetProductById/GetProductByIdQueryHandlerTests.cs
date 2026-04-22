using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.GetProductById;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;

namespace Catalog.UnitTests.Products.GetProductById;

public class GetProductByIdQueryHandlerTests
{
    [Fact]
    public async Task Given_ExistingRow_When_Querying_Then_ReturnsDetailResponse()
    {
        await using var db = FakeCatalogDbContext.Create();
        var row = ProductSearchViewRowBuilder.Active()
            .WithImages(new ImageReferenceDto { Url = "https://cdn.example.com/a.jpg", AltText = "a", DisplayOrder = 0 });
        db.ProductSearchView.Add(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetProductByIdQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetProductByIdQuery { ProductId = row.ProductId },
            TestContext.Current.CancellationToken);

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
        await using var db = FakeCatalogDbContext.Create();
        var handler = new GetProductByIdQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetProductByIdQuery { ProductId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }
}
