using Basket.Domain.Baskets.Errors;

namespace Basket.UnitTests.Baskets.Errors;

public class BasketConcurrencyErrorTests
{
    [Fact]
    public void Fields_AreSurfacedViaMessageAndMetadata()
    {
        var userId = Guid.CreateVersion7();

        var err = new BasketConcurrencyError(userId, Expected: 3, Actual: 5);

        using (new AssertionScope())
        {
            err.UserId.Should().Be(userId);
            err.Expected.Should().Be(3);
            err.Actual.Should().Be(5);
            err.Message.Should().Contain(userId.ToString());
            err.Message.Should().Contain("expected 3");
            err.Message.Should().Contain("found 5");
            err.Metadata.Should().ContainKey("ErrorCode").WhoseValue.Should().Be("Basket.Concurrency");
            err.Reasons.Should().BeEmpty();
        }
    }
}
