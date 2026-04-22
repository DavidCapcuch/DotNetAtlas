using System.Collections.Immutable;
using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class BasketSnapshotTests
{
    [Fact]
    public void Construction_ExposesItemsAndTotal()
    {
        var item = new BasketItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 2);
        var total = new BasketTotal(new Money(20m, CurrencyCode.Usd));

        var snapshot = new BasketSnapshot([item], total);

        using (new AssertionScope())
        {
            snapshot.Items.Should().ContainSingle().Which.Should().Be(item);
            snapshot.Total.Should().Be(total);
        }
    }

    [Fact]
    public void StructuralEquality_SameItemsAndTotal_AreEqual()
    {
        var item = new BasketItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1);
        var total = new BasketTotal(new Money(10m, CurrencyCode.Usd));

        var a = new BasketSnapshot([item], total);
        var b = new BasketSnapshot([item], total);

        using (new AssertionScope())
        {
            a.Should().Be(b);
            a.GetHashCode().Should().Be(b.GetHashCode());
        }
    }

    [Fact]
    public void StructuralEquality_DifferentTotal_AreNotEqual()
    {
        var item = new BasketItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1);

        new BasketSnapshot([item], new BasketTotal(new Money(10m, CurrencyCode.Usd)))
            .Should().NotBe(new BasketSnapshot([item], new BasketTotal(new Money(11m, CurrencyCode.Usd))));
    }

    [Fact]
    public void StructuralEquality_DifferentItemContents_AreNotEqual()
    {
        var productId = Guid.CreateVersion7();
        var total = new BasketTotal(new Money(10m, CurrencyCode.Usd));

        var a = new BasketSnapshot(
            [new BasketItem(productId, BasketTestData.Snapshot(), 1)],
            total);
        var b = new BasketSnapshot(
            [new BasketItem(productId, BasketTestData.Snapshot(), 2)],
            total);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Items_IsImmutableArray_CannotBeMutatedViaDowncast()
    {
        var item = new BasketItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1);
        var snapshot = new BasketSnapshot([item], new BasketTotal(new Money(10m, CurrencyCode.Usd)));

        // ImmutableArray is a value type struct that implements IReadOnlyList but exposes no
        // mutation path — the declared field type itself is the guarantee.
        snapshot.Items.Should().BeOfType<ImmutableArray<BasketItem>>();
    }
}
