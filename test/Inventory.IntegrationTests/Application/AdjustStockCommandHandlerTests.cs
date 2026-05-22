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
/// M7 acceptance for the response-bearing
/// <c>AdjustStockCommandHandler : ICommandHandler&lt;AdjustStockCommand, StockLevelResponse&gt;</c>.
/// Drives a positive and a negative adjustment, asserting the snapshot the
/// HTTP admin endpoint will return matches the post-mutation state.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AdjustStockCommandHandlerTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);

    public AdjustStockCommandHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PositiveDelta_ReflectedInResponse()
    {
        var productId = Guid.CreateVersion7();
        await SeedStreamAsync(productId, onHand: 4);

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AdjustStockCommand, StockLevelResponse>>();

        var result = await handler.HandleAsync(
            new AdjustStockCommand
            {
                ProductId = productId,
                Delta = 3,
                Reason = "recount-add",
                AdjustedByUserId = Guid.CreateVersion7(),
                OccurredOnUtc = UtcNow,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.OnHand.Should().Be(7);
        result.Value.Available.Should().Be(7);
    }

    [Fact]
    public async Task NegativeDelta_ReflectedInResponse()
    {
        var productId = Guid.CreateVersion7();
        await SeedStreamAsync(productId, onHand: 10);

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AdjustStockCommand, StockLevelResponse>>();

        var result = await handler.HandleAsync(
            new AdjustStockCommand
            {
                ProductId = productId,
                Delta = -3,
                Reason = "damage-write-off",
                AdjustedByUserId = Guid.CreateVersion7(),
                OccurredOnUtc = UtcNow,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.OnHand.Should().Be(7);
        result.Value.Available.Should().Be(7);
    }

    private async Task SeedStreamAsync(Guid productId, int onHand)
    {
        using var seedScope = Fixture.CreateScope();
        var init = seedScope.ServiceProvider
            .GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
        var receive = seedScope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();

        (await init.HandleAsync(
            new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = UtcNow.AddMinutes(-2) },
            TestContext.Current.CancellationToken)).Should().BeSuccess();
        (await receive.HandleAsync(
            new ReceiveStockCommand
            {
                ProductId = productId,
                Quantity = onHand,
                Source = "receiving-dock",
                ReceivedByUserId = null,
                OccurredOnUtc = UtcNow.AddMinutes(-1),
            },
            TestContext.Current.CancellationToken)).Should().BeSuccess();
    }
}
