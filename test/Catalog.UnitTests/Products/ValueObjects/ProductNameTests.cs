using Catalog.Domain.Products.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.ValueObjects;

public class ProductNameTests
{
    [Fact]
    public void Create_WhenValid_ReturnsTrimmedName()
    {
        // Act
        var result = ProductName.Create("  Sony WH-1000XM5  ");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be("Sony WH-1000XM5");
        }
    }

    [Fact]
    public void Create_CollapsesInternalWhitespace()
    {
        // Act
        var result = ProductName.Create("Sony   WH   1000XM5");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be("Sony WH 1000XM5");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenEmpty_ReturnsFailureWithProductNameEmpty(string? input)
    {
        // Act
        var result = ProductName.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ProductName.Empty");
        }
    }

    [Fact]
    public void Create_WhenLongerThan200_ReturnsFailureWithProductNameTooLong()
    {
        // Arrange
        var tooLong = new string('A', 201);

        // Act
        var result = ProductName.Create(tooLong);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ProductName.TooLong");
        }
    }
}
