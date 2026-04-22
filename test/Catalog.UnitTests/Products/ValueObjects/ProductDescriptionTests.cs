using Catalog.Domain.Products.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.ValueObjects;

public class ProductDescriptionTests
{
    [Fact]
    public void Create_WhenValid_ReturnsDescription()
    {
        // Act
        var result = ProductDescription.Create("A high-end wireless headphone.");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be("A high-end wireless headphone.");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_WhenEmptyOrNull_ReturnsEmptyDescription(string? input)
    {
        // Act
        var result = ProductDescription.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().BeEmpty();
        }
    }

    [Fact]
    public void Create_WhenLongerThan4000_ReturnsFailureWithProductDescriptionTooLong()
    {
        // Arrange
        var tooLong = new string('A', 4001);

        // Act
        var result = ProductDescription.Create(tooLong);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ProductDescription.TooLong");
        }
    }
}
