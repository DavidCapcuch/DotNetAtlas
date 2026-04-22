using Catalog.Domain.Products.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.ValueObjects;

public class SkuTests
{
    [Fact]
    public void Create_WhenValid_ReturnsUppercasedSku()
    {
        // Act
        var result = Sku.Create("abc-123");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be("ABC-123");
        }
    }

    [Fact]
    public void Create_WhenTrimmed_ReturnsValueWithoutWhitespace()
    {
        // Act
        var result = Sku.Create("  sku-1  ");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be("SKU-1");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenEmpty_ReturnsFailureWithSkuEmpty(string? input)
    {
        // Act
        var result = Sku.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Sku.Empty");
        }
    }

    [Fact]
    public void Create_WhenLongerThan32_ReturnsFailureWithSkuTooLong()
    {
        // Arrange
        var tooLong = new string('A', 33);

        // Act
        var result = Sku.Create(tooLong);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Sku.TooLong");
        }
    }

    [Theory]
    [InlineData("-starts-with-dash")]
    [InlineData("has space")]
    [InlineData("has*asterisk")]
    [InlineData("has/slash")]
    public void Create_WhenInvalidCharacters_ReturnsFailureWithInvalidCharacters(string input)
    {
        // Act
        var result = Sku.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Sku.InvalidCharacters");
        }
    }

    [Fact]
    public void Create_AtBoundary32_ReturnsSuccess()
    {
        // Arrange
        var thirtyTwo = new string('A', 32);

        // Act
        var result = Sku.Create(thirtyTwo);

        // Assert
        result.Should().BeSuccess();
    }
}
