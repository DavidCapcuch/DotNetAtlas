using Catalog.Domain.Products.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.ValueObjects;

public class DimensionsTests
{
    [Theory]
    [InlineData("cm", "cm")]
    [InlineData("mm", "mm")]
    [InlineData("in", "in")]
    [InlineData("CM", "cm")]
    [InlineData("Mm", "mm")]
    public void Create_WhenValid_ReturnsCanonicalLowercaseUnit(string input, string expectedCanonicalUnit)
    {
        // Act
        var result = Dimensions.Create(10m, 5m, 2m, input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Unit.Should().Be(expectedCanonicalUnit);
            result.Value.Length.Should().Be(10m);
            result.Value.Width.Should().Be(5m);
            result.Value.Height.Should().Be(2m);
        }
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(-1, 1, 1)]
    public void Create_WhenNonPositiveDimension_ReturnsFailure(decimal length, decimal width, decimal height)
    {
        // Act
        var result = Dimensions.Create(length, width, height, "cm");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Dimensions.NonPositiveDimension");
        }
    }

    [Theory]
    [InlineData("foot")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_WhenUnsupportedUnit_ReturnsFailure(string? unit)
    {
        // Act
        var result = Dimensions.Create(1m, 1m, 1m, unit);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Dimensions.UnsupportedUnit");
        }
    }
}
