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
    public async Task Handle_MixOfKnownAndUnknown_ReturnsPartialResult()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var existing = ProductSearchViewRowBuilder.Active("A-1");
        await seeder.SeedRowsAsync(ct, existing);

        var missingId = Guid.CreateVersion7();
        var handler = new GetProductsByIdsQueryHandler(CatalogDbContext);

        // Act
        var result = await handler.HandleAsync(
            new GetProductsByIdsQuery { Ids = [existing.ProductId, missingId] }, ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Products.Should().ContainSingle().Which.ProductId.Should().Be(existing.ProductId);
            result.Value.MissingProductIds.Should().ContainSingle().Which.Should().Be(missingId);
        }
    }

    [Fact]
    public async Task Handle_AllUnknown_ReturnsEmptyProductsAndAllMissing()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var handler = new GetProductsByIdsQueryHandler(CatalogDbContext);
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        // Act
        var result = await handler.HandleAsync(new GetProductsByIdsQuery { Ids = [a, b] }, ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Products.Should().BeEmpty();
            result.Value.MissingProductIds.Should().BeEquivalentTo([a, b]);
        }
    }
}
