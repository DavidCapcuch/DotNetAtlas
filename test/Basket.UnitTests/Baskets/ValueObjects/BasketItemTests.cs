using Basket.Domain.Baskets.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class BasketItemTests
{
    [Fact]
    public void StructuralEquality_DifferentInstancesWithSameValues_AreEqual()
    {
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();

        var a = BasketItem.BuildUnchecked(productId, snapshot, 3);
        var b = BasketItem.BuildUnchecked(productId, snapshot, 3);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void StructuralEquality_DifferentQuantity_AreNotEqual()
    {
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();

        BasketItem.BuildUnchecked(productId, snapshot, 1)
            .Should().NotBe(BasketItem.BuildUnchecked(productId, snapshot, 2));
    }
}
