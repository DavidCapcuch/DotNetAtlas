using Catalog.Domain.Products.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.ValueObjects;

public class BrandNameTests
{
    [Fact]
    public void Create_WhenValid_ReturnsBrand()
    {
        // Act
        var result = BrandName.Create("Sony");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be("Sony");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenEmpty_ReturnsFailureWithBrandNameEmpty(string? input)
    {
        // Act
        var result = BrandName.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "BrandName.Empty");
        }
    }

    [Fact]
    public void Create_WhenLongerThan100_ReturnsFailureWithBrandNameTooLong()
    {
        // Arrange
        var tooLong = new string('A', 101);

        // Act
        var result = BrandName.Create(tooLong);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "BrandName.TooLong");
        }
    }
}
