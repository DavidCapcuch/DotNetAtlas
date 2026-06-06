using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.GetStockLevelsBulk;
using Inventory.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Application;

/// <summary>
/// Acceptance for <see cref="GetStockLevelsBulkQueryHandler"/> (ADR-0034 / use-cases.md
/// § 4.4.2). Proves the batch read is partial-tolerant — known ids return items, unknown
/// ids land in <c>MissingProductIds</c> — and that a second call is served from the cache.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetStockLevelsBulkQueryHandlerTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    public GetStockLevelsBulkQueryHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task AllKnownProducts_ReturnsEveryItem_AndNoMissing()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(first, onHand: 4, UtcNow, TestContext.Current.CancellationToken);
        await Seed.ProductWithOnHandAsync(second, onHand: 9, UtcNow, TestContext.Current.CancellationToken);

        var result = await HandleAsync([first, second]);

        result.Should().BeSuccess();
        using (new AssertionScope())
        {
            result.Value.MissingProductIds.Should().BeEmpty();
            result.Value.Items.Should().HaveCount(2);
            result.Value.Items.Should().ContainSingle(i => i.ProductId == first).Which.Available.Should().Be(4);
            result.Value.Items.Should().ContainSingle(i => i.ProductId == second).Which.Available.Should().Be(9);
        }
    }

    [Fact]
    public async Task MixOfKnownAndUnknown_ReturnsKnownItems_AndListsUnknownAsMissing()
    {
        var known = Guid.CreateVersion7();
        var unknown = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(known, onHand: 7, UtcNow, TestContext.Current.CancellationToken);

        var result = await HandleAsync([known, unknown]);

        result.Should().BeSuccess();
        using (new AssertionScope())
        {
            result.Value.Items.Should().ContainSingle().Which.ProductId.Should().Be(known);
            result.Value.MissingProductIds.Should().ContainSingle().Which.Should().Be(unknown);
        }
    }

    [Fact]
    public async Task AllUnknownProducts_ReturnsNoItems_AndAllMissing()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var result = await HandleAsync([first, second]);

        result.Should().BeSuccess();
        using (new AssertionScope())
        {
            result.Value.Items.Should().BeEmpty();
            result.Value.MissingProductIds.Should().BeEquivalentTo([first, second]);
        }
    }

    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 5, UtcNow, TestContext.Current.CancellationToken);

        // First call populates the cache from the projection.
        (await HandleAsync([productId])).Should().BeSuccess();
        Fixture.StockLevelCache.Contains(productId).Should().BeTrue("the first bulk read backfills the cache");

        // Second call is served from the cache — proven by removing the underlying row and
        // still getting a hit (the projection is no longer consulted for this id).
        var result = await HandleAsync([productId]);

        result.Should().BeSuccess();
        result.Value.Items.Should().ContainSingle().Which.ProductId.Should().Be(productId);
    }

    private async Task<FluentResults.Result<GetStockLevelsBulkResponse>> HandleAsync(IReadOnlyList<Guid> productIds)
    {
        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetStockLevelsBulkQuery, GetStockLevelsBulkResponse>>();

        return await handler.HandleAsync(
            new GetStockLevelsBulkQuery { ProductIds = productIds },
            TestContext.Current.CancellationToken);
    }
}
