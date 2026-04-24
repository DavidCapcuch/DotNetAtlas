using FluentResults.Extensions.FluentAssertions;
using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Inventory.UnitTests.StockItems.ValueObjects;

public class QuantityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Create_WhenNonNegative_ReturnsSuccess(int value)
    {
        // Act
        var result = Quantity.Create(value);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be(value);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_WhenNegative_ReturnsFailureWithQuantityNegative(int value)
    {
        // Act
        var result = Quantity.Create(value);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Quantity.Negative");
        }
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        // Arrange
        var a = Quantity.Create(5).Value;
        var b = Quantity.Create(5).Value;

        // Assert
        a.Should().Be(b);
    }
}
