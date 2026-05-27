using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class BasketTotalTests
{
    [Fact]
    public void From_ExposesAmount()
    {
        var money = Money.Create(123.45m, CurrencyCode.Gbp).Value;

        var total = BasketTotal.From(money);

        total.Amount.Should().Be(money);
    }

    [Fact]
    public void StructuralEquality_SameAmount_AreEqual()
    {
        var money = Money.Create(123.45m, CurrencyCode.Gbp).Value;

        BasketTotal.From(money).Should().Be(BasketTotal.From(money));
    }

    [Fact]
    public void StructuralEquality_DifferentCurrency_AreNotEqual()
    {
        BasketTotal.From(Money.Create(10m, CurrencyCode.Usd).Value)
            .Should().NotBe(BasketTotal.From(Money.Create(10m, CurrencyCode.Eur).Value));
    }
}
