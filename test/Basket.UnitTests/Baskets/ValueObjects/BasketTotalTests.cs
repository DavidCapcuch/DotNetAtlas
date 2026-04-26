using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class BasketTotalTests
{
    [Fact]
    public void From_ExposesAmount()
    {
        var money = new Money(123.45m, CurrencyCode.Gbp);

        var total = BasketTotal.From(money);

        total.Amount.Should().Be(money);
    }

    [Fact]
    public void StructuralEquality_SameAmount_AreEqual()
    {
        var money = new Money(123.45m, CurrencyCode.Gbp);

        BasketTotal.From(money).Should().Be(BasketTotal.From(money));
    }

    [Fact]
    public void StructuralEquality_DifferentCurrency_AreNotEqual()
    {
        BasketTotal.From(new Money(10m, CurrencyCode.Usd))
            .Should().NotBe(BasketTotal.From(new Money(10m, CurrencyCode.Eur)));
    }
}
