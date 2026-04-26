using FluentResults.Extensions.FluentAssertions;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Inventory.IntegrationTests.Application;

/// <summary>
/// M7 acceptance for the response-bearing
/// <c>ReceiveStockCommandHandler : ICommandHandler&lt;ReceiveStockCommand, StockLevelResponse&gt;</c>.
/// Proves the handler appends the ES event AND returns the post-mutation
/// <see cref="StockLevelResponse"/> matching the projection row.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ReceiveStockCommandHandlerTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IntegrationTestFixture _fixture;

    public ReceiveStockCommandHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_ReturnsPostMutationStockLevelSnapshot()
    {
        var productId = Guid.CreateVersion7();

        using (var seedScope = _fixture.CreateScope())
        {
            var init = seedScope.ServiceProvider
                .GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
            (await init.HandleAsync(
                new InitializeStockItemCommand
                {
                    ProductId = productId,
                    OccurredOnUtc = UtcNow.AddMinutes(-1),
                },
                TestContext.Current.CancellationToken)).Should().BeSuccess();
        }

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();

        var result = await handler.HandleAsync(
            new ReceiveStockCommand
            {
                ProductId = productId,
                Quantity = 7,
                Source = "receiving-dock",
                ReceivedByUserId = null,
                OccurredOnUtc = UtcNow,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();

        using (new AssertionScope())
        {
            var response = result.Value;
            response.ProductId.Should().Be(productId);
            response.OnHand.Should().Be(7);
            response.Reserved.Should().Be(0);
            response.Available.Should().Be(7);
            response.LastUpdatedUtc.Should().Be(UtcNow);
            response.LastVersion.Should().BeGreaterThan(0);
        }

        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var row = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        row.OnHand.Should().Be(7);
        row.Available.Should().Be(7);
    }
}
