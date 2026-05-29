using Catalog.Application.Products.GetProductsByIds;
using Catalog.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;

namespace Catalog.IntegrationTests.Products.GetProductsByIds;

[Collection<IntegrationTestCollection>]
public sealed class GetProductsByIdsQueryHandlerTests : BaseIntegrationTest
{
    public GetProductsByIdsQueryHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task Given_MixOfKnownAndUnknown_Then_ReturnsPartialResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var existing = ProductSearchViewRowBuilder.Active("A-1");
        await seeder.SeedRowsAsync(ct, existing);

        var missingId = Guid.CreateVersion7();
        var handler = new GetProductsByIdsQueryHandler(CatalogDbContext);

        var result = await handler.HandleAsync(
            new GetProductsByIdsQuery { Ids = [existing.ProductId, missingId] }, ct);

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
        var ct = TestContext.Current.CancellationToken;
        var handler = new GetProductsByIdsQueryHandler(CatalogDbContext);
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var result = await handler.HandleAsync(new GetProductsByIdsQuery { Ids = [a, b] }, ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Products.Should().BeEmpty();
            result.Value.MissingProductIds.Should().BeEquivalentTo([a, b]);
        }
    }
}
