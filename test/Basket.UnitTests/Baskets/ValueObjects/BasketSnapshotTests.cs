using System.Collections.Immutable;
using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class BasketSnapshotTests
{
    [Fact]
    public void Create_ExposesItemsAndTotal()
    {
        var item = BasketItem.Create(Guid.CreateVersion7(), BasketTestData.Snapshot(), 2).Value;
        var total = BasketTotal.From(new Money(20m, CurrencyCode.Usd));

        var snapshot = BasketSnapshot.Create([item], total);

        using (new AssertionScope())
        {
            snapshot.Items.Should().ContainSingle().Which.Should().Be(item);
            snapshot.Total.Should().Be(total);
        }
    }

    [Fact]
    public void StructuralEquality_SameItemsAndTotal_AreEqual()
    {
        var item = BasketItem.Create(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1).Value;
        var total = BasketTotal.From(new Money(10m, CurrencyCode.Usd));

        var a = BasketSnapshot.Create([item], total);
        var b = BasketSnapshot.Create([item], total);

        using (new AssertionScope())
        {
            a.Should().Be(b);
            a.GetHashCode().Should().Be(b.GetHashCode());
        }
    }

    [Fact]
    public void StructuralEquality_DifferentTotal_AreNotEqual()
    {
        var item = BasketItem.Create(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1).Value;

        BasketSnapshot.Create([item], BasketTotal.From(new Money(10m, CurrencyCode.Usd)))
            .Should().NotBe(BasketSnapshot.Create([item], BasketTotal.From(new Money(11m, CurrencyCode.Usd))));
    }

    [Fact]
    public void StructuralEquality_DifferentItemContents_AreNotEqual()
    {
        var productId = Guid.CreateVersion7();
        var total = BasketTotal.From(new Money(10m, CurrencyCode.Usd));

        var a = BasketSnapshot.Create(
            [BasketItem.Create(productId, BasketTestData.Snapshot(), 1).Value],
            total);
        var b = BasketSnapshot.Create(
            [BasketItem.Create(productId, BasketTestData.Snapshot(), 2).Value],
            total);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Items_IsImmutableArray_CannotBeMutatedViaDowncast()
    {
        var item = BasketItem.Create(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1).Value;
        var snapshot = BasketSnapshot.Create([item], BasketTotal.From(new Money(10m, CurrencyCode.Usd)));

        // ImmutableArray is a value type struct that implements IReadOnlyList but exposes no
        // mutation path — the declared field type itself is the guarantee.
        snapshot.Items.Should().BeOfType<ImmutableArray<BasketItem>>();
    }
}
