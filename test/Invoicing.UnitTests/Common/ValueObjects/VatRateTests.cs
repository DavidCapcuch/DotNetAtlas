using Invoicing.Domain.Common.ValueObjects;

namespace Invoicing.UnitTests.Common.ValueObjects;

public class VatRateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(21.5)]
    [InlineData(100)]
    public void Create_AcceptsValidPercentages(decimal pct)
    {
        VatRate.Create(pct).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Create_RejectsOutOfRange(decimal pct)
    {
        VatRate.Create(pct).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_RejectsMoreThanTwoDecimals()
    {
        VatRate.Create(19.995m).IsSuccess.Should().BeFalse();
    }
}
