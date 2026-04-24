using FluentResults.Extensions.FluentAssertions;
using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Inventory.UnitTests.StockItems.ValueObjects;

public class StockSourceTests
{
    [Fact]
    public void Create_WhenValid_TrimsAndReturnsSuccess()
    {
        // Act
        var result = StockSource.Create("  receiving-dock  ");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be("receiving-dock");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenEmpty_ReturnsFailureWithStockSourceEmpty(string? input)
    {
        // Act
        var result = StockSource.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "StockSource.Empty");
        }
    }

    [Fact]
    public void Create_WhenLongerThanMax_ReturnsFailureWithStockSourceTooLong()
    {
        // Arrange
        var tooLong = new string('x', StockSource.MaxLength + 1);

        // Act
        var result = StockSource.Create(tooLong);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "StockSource.TooLong");
        }
    }

    [Fact]
    public void Create_AtBoundary_ReturnsSuccess()
    {
        // Arrange
        var atBoundary = new string('x', StockSource.MaxLength);

        // Act
        var result = StockSource.Create(atBoundary);

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        // Assert — canonical tokens documented in inventory.md § 4.
        using (new AssertionScope())
        {
            StockSource.ReceivingDock.Value.Should().Be("receiving-dock");
            StockSource.Returns.Value.Should().Be("returns");
            StockSource.TransferIn.Value.Should().Be("transfer-in");
        }
    }
}
