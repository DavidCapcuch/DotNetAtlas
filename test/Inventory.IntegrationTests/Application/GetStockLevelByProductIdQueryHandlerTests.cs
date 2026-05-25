using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.GetStockLevelByProductId;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Platform.SharedKernel.Errors;

namespace Inventory.IntegrationTests.Application;

/// <summary>
/// M7 acceptance for <see cref="GetStockLevelByProductIdQueryHandler"/>. Proves
/// the read-side returns the projection row when present and a typed
/// <see cref="NotFoundError"/> when absent.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetStockLevelByProductIdQueryHandlerTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);

    public GetStockLevelByProductIdQueryHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ExistingProduct_ReturnsProjectionSnapshot()
    {
        var productId = Guid.CreateVersion7();

        using (var seedScope = Fixture.CreateScope())
        {
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
                    Quantity = 9,
                    Source = "receiving-dock",
                    ReceivedByUserId = null,
                    OccurredOnUtc = UtcNow.AddMinutes(-1),
                },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetStockLevelByProductIdQuery, StockLevelResponse>>();

        var result = await handler.HandleAsync(
            new GetStockLevelByProductIdQuery { ProductId = productId },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.ProductId.Should().Be(productId);
        result.Value.OnHand.Should().Be(9);
        result.Value.Available.Should().Be(9);
    }

    [Fact]
    public async Task UnknownProduct_ReturnsNotFoundError()
    {
        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetStockLevelByProductIdQuery, StockLevelResponse>>();

        var result = await handler.HandleAsync(
            new GetStockLevelByProductIdQuery { ProductId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<NotFoundError>()
            .Which.ErrorCode.Should().Be("Inventory.StockItem.NotFound");
    }
}
