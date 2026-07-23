using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Platform.SharedKernel.UnitTests.ValueObjects;

public class MoneyTests
{
    [Fact]
    public void Create_WithCurrencyCode_WhenPositiveAmount_ReturnsSuccess()
    {
        // Arrange & Act
        var result = Money.Create(12.34m, CurrencyCode.Usd);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Amount.Should().Be(12.34m);
            result.Value.Currency.Should().Be(CurrencyCode.Usd);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Create_WithCurrencyCode_WhenZeroOrNegativeAmount_ReturnsSuccess(decimal amount)
    {
        // Money is a currency-tagged signed decimal — sign is an aggregate-level concern,
        // not Money's own invariant. Zero and negative amounts construct successfully.

        // Arrange & Act
        var result = Money.Create(amount, CurrencyCode.Eur);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Amount.Should().Be(amount);
            result.Value.Currency.Should().Be(CurrencyCode.Eur);
        }
    }

    [Fact]
    public void Create_WithCurrencyCode_WhenCurrencyIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => Money.Create(1m, (CurrencyCode)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("usd")]
    [InlineData("Eur")]
    [InlineData("cZk")]
    public void Create_WithStringCode_WhenKnownCode_ResolvesCaseInsensitively(string code)
    {
        // Arrange & Act
        var result = Money.Create(1m, code);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Currency.Name.Should().Be(code.ToUpperInvariant());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("US")]
    [InlineData("USDX")]
    public void Create_WithStringCode_WhenInvalidLength_ReturnsInvalidCurrencyCode(string? code)
    {
        // Arrange & Act
        var result = Money.Create(1m, code!);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Money.InvalidCurrencyCode");
        }
    }

    [Fact]
    public void Create_WithStringCode_WhenUnknownCode_ReturnsUnknownCurrencyCode()
    {
        // Arrange & Act
        var result = Money.Create(1m, "XYZ");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Money.UnknownCurrencyCode");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void Create_WithStringCode_WhenZeroOrNegativeAmount_ReturnsSuccess(decimal amount)
    {
        // Sign is not Money's concern.

        // Arrange & Act
        var result = Money.Create(amount, "USD");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Amount.Should().Be(amount);
            result.Value.Currency.Should().Be(CurrencyCode.Usd);
        }
    }

    [Fact]
    public void Zero_WithValidCurrency_ReturnsAmountZero()
    {
        // Arrange & Act
        var zero = Money.Zero(CurrencyCode.Czk);

        // Assert
        using (new AssertionScope())
        {
            zero.Amount.Should().Be(0m);
            zero.Currency.Should().Be(CurrencyCode.Czk);
        }
    }

    [Fact]
    public void Zero_WhenCurrencyIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => Money.Zero(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Negate_WhenPositive_ReturnsNegativeWithSameCurrency()
    {
        // Arrange
        var positive = Money.Create(12.50m, CurrencyCode.Gbp).Value;

        // Act
        var negated = positive.Negate();

        // Assert
        using (new AssertionScope())
        {
            negated.Amount.Should().Be(-12.50m);
            negated.Currency.Should().Be(CurrencyCode.Gbp);
        }
    }

    [Fact]
    public void Negate_WhenNegative_ReturnsPositiveWithSameCurrency()
    {
        // Arrange
        var negative = Money.Create(-7m, CurrencyCode.Eur).Value;

        // Act
        var negated = negative.Negate();

        // Assert
        using (new AssertionScope())
        {
            negated.Amount.Should().Be(7m);
            negated.Currency.Should().Be(CurrencyCode.Eur);
        }
    }

    [Fact]
    public void Negate_AppliedTwice_IsIdempotentWithOriginal()
    {
        // Arrange
        var original = Money.Create(3.14m, CurrencyCode.Usd).Value;

        // Act
        var twice = original.Negate().Negate();

        // Assert
        twice.Should().Be(original);
    }

    [Fact]
    public void WithAmount_ReturnsSameCurrencyWithNewAmount()
    {
        // Arrange
        var original = Money.Create(10m, CurrencyCode.Gbp).Value;

        // Act
        var repriced = original.WithAmount(99.99m);

        // Assert
        using (new AssertionScope())
        {
            repriced.Amount.Should().Be(99.99m);
            repriced.Currency.Should().Be(CurrencyCode.Gbp);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void WithAmount_WhenZeroOrNegative_StaysPermissive(decimal amount)
    {
        // WithAmount carries the aggregate's positivity rule nowhere — sign is a holder-aggregate
        // concern (School B), so a guard here would duplicate Product.UpdatePrice's Amount > 0 check.

        // Arrange
        var original = Money.Create(10m, CurrencyCode.Eur).Value;

        // Act
        var repriced = original.WithAmount(amount);

        // Assert
        using (new AssertionScope())
        {
            repriced.Amount.Should().Be(amount);
            repriced.Currency.Should().Be(CurrencyCode.Eur);
        }
    }

    [Fact]
    public void OperatorPlus_WhenSameCurrency_SumsAmounts()
    {
        // Arrange
        var left = Money.Create(10m, CurrencyCode.Eur).Value;
        var right = Money.Create(2.5m, CurrencyCode.Eur).Value;

        // Act
        var sum = left + right;

        // Assert
        using (new AssertionScope())
        {
            sum.Amount.Should().Be(12.5m);
            sum.Currency.Should().Be(CurrencyCode.Eur);
        }
    }

    [Fact]
    public void OperatorMinus_WhenSameCurrency_SubtractsAmounts()
    {
        // Arrange
        var left = Money.Create(10m, CurrencyCode.Gbp).Value;
        var right = Money.Create(3m, CurrencyCode.Gbp).Value;

        // Act
        var diff = left - right;

        // Assert
        using (new AssertionScope())
        {
            diff.Amount.Should().Be(7m);
            diff.Currency.Should().Be(CurrencyCode.Gbp);
        }
    }

    [Fact]
    public void OperatorPlus_WhenCurrenciesDiffer_ThrowsWithBothCurrencyNames()
    {
        // Arrange
        var usd = Money.Create(5m, CurrencyCode.Usd).Value;
        var eur = Money.Create(5m, CurrencyCode.Eur).Value;

        // Act
        var act = () => { _ = usd + eur; };

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*USD*EUR*");
    }

    [Fact]
    public void OperatorMinus_WhenCurrenciesDiffer_ThrowsInvalidOperation()
    {
        // Arrange
        var usd = Money.Create(5m, CurrencyCode.Usd).Value;
        var eur = Money.Create(5m, CurrencyCode.Eur).Value;

        // Act
        var act = () => { _ = usd - eur; };

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }
}
