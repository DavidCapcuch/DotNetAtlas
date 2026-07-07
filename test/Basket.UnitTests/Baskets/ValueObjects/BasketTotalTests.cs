using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class BasketTotalTests
{
    [Fact]
    public void From_WhenStrictlyPositiveAmount_ExposesAmount()
    {
        // Arrange
        var money = Money.Create(123.45m, CurrencyCode.Gbp).Value;

        // Act
        var total = BasketTotal.From(money);

        // Assert
        total.Amount.Should().Be(money);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [Trait("Category", "boundary")]
    public void From_WhenNonPositiveAmount_ThrowsDataIntegrityException(decimal nonPositive)
    {
        // BasketTotal wraps a strictly-positive Money — a zero/negative total indicates
        // a caller bug (the aggregate only builds totals from validated line items).
        // Money itself is sign-neutral (School B), so the rule lives on this VO.

        // Arrange
        var money = Money.Create(nonPositive, CurrencyCode.Gbp).Value;

        // Act
        var act = () => BasketTotal.From(money);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Basket.NonPositiveTotal");
    }
}
