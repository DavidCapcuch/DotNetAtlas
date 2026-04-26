using Inventory.Application.Common.ReadModels;
using Inventory.Application.StockItems.Common;

namespace Inventory.UnitTests.StockItems.Common;

public sealed class StockLevelResponseMapperTests
{
    [Fact]
    public void ToStockLevelResponse_CopiesPublicFields_OmitsPreviousAvailable()
    {
        var productId = Guid.CreateVersion7();
        var lastUpdated = new DateTimeOffset(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);
        var row = new CurrentStockLevelRow
        {
            ProductId = productId,
            OnHand = 12,
            Reserved = 3,
            Available = 9,
            PreviousAvailable = 11,
            LastUpdatedUtc = lastUpdated,
            LastVersion = 7,
        };

        var response = row.ToStockLevelResponse();

        using (new AssertionScope())
        {
            response.ProductId.Should().Be(productId);
            response.OnHand.Should().Be(12);
            response.Reserved.Should().Be(3);
            response.Available.Should().Be(9);
            response.LastUpdatedUtc.Should().Be(lastUpdated);
            response.LastVersion.Should().Be(7);
        }
    }
}
