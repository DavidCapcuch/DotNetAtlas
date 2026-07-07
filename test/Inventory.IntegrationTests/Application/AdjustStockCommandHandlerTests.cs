using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.AdjustStock;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Application;

/// <summary>
/// Acceptance for the response-bearing
/// <c>AdjustStockCommandHandler : ICommandHandler&lt;AdjustStockCommand, StockLevelResponse&gt;</c>.
/// Drives a positive and a negative adjustment, asserting the snapshot the
/// HTTP admin endpoint will return matches the post-mutation state.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class AdjustStockCommandHandlerTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);

    public AdjustStockCommandHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    // Positive and negative deltas exercise the same handler → StockLevelResponse snapshot
    // wiring; the sign-specific aggregate arithmetic (and the below-zero / below-reservations
    // guards) is covered at the unit tier in StockItemTests.AdjustStock_*.
    [Theory]
    [InlineData(4, 3, 7, "recount-add")] // positive delta: 4 + 3
    [InlineData(10, -3, 7, "damage-write-off")] // negative delta: 10 - 3
    public async Task Adjust_ReflectsPostMutationSnapshotInResponse(
        int startOnHand, int delta, int expectedOnHand, string reason)
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, startOnHand, UtcNow.AddMinutes(-2), TestContext.Current.CancellationToken);

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AdjustStockCommand, StockLevelResponse>>();

        // Act
        var result = await handler.HandleAsync(
            new AdjustStockCommand
            {
                ProductId = productId,
                Delta = delta,
                Reason = reason,
                AdjustedByUserId = Guid.CreateVersion7(),
                OccurredOnUtc = UtcNow,
            },
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
        using (new AssertionScope())
        {
            result.Value.OnHand.Should().Be(expectedOnHand);
            result.Value.Available.Should().Be(expectedOnHand);
        }
    }
}
