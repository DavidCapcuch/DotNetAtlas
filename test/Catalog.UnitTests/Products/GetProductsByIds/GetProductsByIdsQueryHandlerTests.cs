using Catalog.Application.Products.GetProductsByIds;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;

namespace Catalog.UnitTests.Products.GetProductsByIds;

public class GetProductsByIdsQueryHandlerTests
{
    [Fact]
    public async Task Given_MixOfKnownAndUnknown_Then_ReturnsPartialResult()
    {
        await using var db = FakeCatalogDbContext.Create();
        var existing = ProductSearchViewRowBuilder.Active("A-1");
        db.ProductSearchView.Add(existing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var missingId = Guid.CreateVersion7();
        var handler = new GetProductsByIdsQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetProductsByIdsQuery { Ids = [existing.ProductId, missingId] },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Products.Should().ContainSingle().Which.ProductId.Should().Be(existing.ProductId);
            result.Value.MissingProductIds.Should().ContainSingle().Which.Should().Be(missingId);
        }
    }

    [Fact]
    public async Task Given_AllUnknown_Then_ReturnsEmptyProductsAndAllMissing()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new GetProductsByIdsQueryHandler(db);
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var result = await handler.HandleAsync(
            new GetProductsByIdsQuery { Ids = [a, b] },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Products.Should().BeEmpty();
            result.Value.MissingProductIds.Should().BeEquivalentTo([a, b]);
        }
    }
}
