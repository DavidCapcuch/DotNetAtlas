using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

/// <summary>
/// <see cref="BasketSnapshot"/> hand-writes <c>Equals</c>/<c>GetHashCode</c> (element-wise
/// <c>SequenceEqual</c> over Items) because the compiler-synthesized record equality would
/// compare <c>ImmutableArray</c> by reference. These tests pin that hand-written contract.
/// </summary>
public class BasketSnapshotTests
{
    [Fact]
    public void Create_WhenGivenItemsAndTotal_ExposesBoth()
    {
        // Arrange
        var item = BasketItem.BuildUnchecked(Guid.CreateVersion7(), BasketTestData.Snapshot(), 2);
        var total = BasketTotal.From(Money.Create(20m, CurrencyCode.Usd).Value);

        // Act
        var snapshot = BasketSnapshot.Create([item], total);

        // Assert
        using (new AssertionScope())
        {
            snapshot.Items.Should().ContainSingle().Which.Should().Be(item);
            snapshot.Total.Should().Be(total);
        }
    }

    [Fact]
    public void StructuralEquality_SameItemsAndTotal_AreEqual()
    {
        // Arrange — two snapshots over distinct backing arrays holding equal data.
        var item = BasketItem.BuildUnchecked(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1);
        var total = BasketTotal.From(Money.Create(10m, CurrencyCode.Usd).Value);
        var first = BasketSnapshot.Create([item], total);
        var second = BasketSnapshot.Create([item], total);

        // Assert
        using (new AssertionScope())
        {
            first.Should().Be(second);
            first.GetHashCode().Should().Be(second.GetHashCode());
        }
    }

    [Fact]
    public void StructuralEquality_DifferentTotal_AreNotEqual()
    {
        // Arrange
        var item = BasketItem.BuildUnchecked(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1);
        var first = BasketSnapshot.Create([item], BasketTotal.From(Money.Create(10m, CurrencyCode.Usd).Value));
        var second = BasketSnapshot.Create([item], BasketTotal.From(Money.Create(11m, CurrencyCode.Usd).Value));

        // Assert
        first.Should().NotBe(second);
    }

    [Fact]
    public void StructuralEquality_DifferentItemContents_AreNotEqual()
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        var total = BasketTotal.From(Money.Create(10m, CurrencyCode.Usd).Value);
        var first = BasketSnapshot.Create(
            [BasketItem.BuildUnchecked(productId, BasketTestData.Snapshot(), 1)],
            total);
        var second = BasketSnapshot.Create(
            [BasketItem.BuildUnchecked(productId, BasketTestData.Snapshot(), 2)],
            total);

        // Assert
        first.Should().NotBe(second);
    }
}
