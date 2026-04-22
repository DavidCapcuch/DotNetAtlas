using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class BasketTotalTests
{
    [Fact]
    public void Construction_ExposesAmount()
    {
        var money = new Money(123.45m, CurrencyCode.Gbp);

        var total = new BasketTotal(money);

        total.Amount.Should().Be(money);
    }

    [Fact]
    public void StructuralEquality_SameAmount_AreEqual()
    {
        var money = new Money(123.45m, CurrencyCode.Gbp);

        new BasketTotal(money).Should().Be(new BasketTotal(money));
    }

    [Fact]
    public void StructuralEquality_DifferentCurrency_AreNotEqual()
    {
        new BasketTotal(new Money(10m, CurrencyCode.Usd))
            .Should().NotBe(new BasketTotal(new Money(10m, CurrencyCode.Eur)));
    }
}
