using Platform.SharedKernel.ValueObjects;

namespace Platform.SharedKernel.UnitTests.ValueObjects;

public class CurrencyCodeTests
{
    [Fact]
    public void TryFromName_WhenExactMatch_ReturnsExpectedInstance()
    {
        // Arrange & Act
        var ok = CurrencyCode.TryFromName("USD", out var currency);

        // Assert
        using (new AssertionScope())
        {
            ok.Should().BeTrue();
            currency.Should().Be(CurrencyCode.Usd);
        }
    }

    [Fact]
    public void TryFromName_WhenLowerCaseAndCaseSensitive_ReturnsFalse()
    {
        // Arrange & Act — default overload is case-sensitive; Money.Create normalises before calling.
        var ok = CurrencyCode.TryFromName("usd", out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryFromName_WhenLowerCaseAndIgnoreCase_ReturnsTrue()
    {
        // Arrange & Act
        var ok = CurrencyCode.TryFromName("usd", ignoreCase: true, out var currency);

        // Assert
        using (new AssertionScope())
        {
            ok.Should().BeTrue();
            currency.Should().Be(CurrencyCode.Usd);
        }
    }

    [Fact]
    public void TryFromName_WhenUnknownCode_ReturnsFalse()
    {
        // Arrange & Act
        var ok = CurrencyCode.TryFromName("XYZ", out _);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void List_ContainsExpectedThirteenCurrencies()
    {
        // Arrange & Act
        var all = CurrencyCode.List;

        // Assert
        all.Should().HaveCount(13);
    }

    [Theory]
    [InlineData("USD", 840)]
    [InlineData("EUR", 978)]
    [InlineData("GBP", 826)]
    [InlineData("CZK", 203)]
    [InlineData("JPY", 392)]
    public void Value_MatchesIso4217NumericCode(string name, int expectedNumeric)
    {
        // Arrange
        var ok = CurrencyCode.TryFromName(name, out var currency);

        // Act & Assert
        using (new AssertionScope())
        {
            ok.Should().BeTrue();
            currency.Value.Should().Be(expectedNumeric);
        }
    }
}
