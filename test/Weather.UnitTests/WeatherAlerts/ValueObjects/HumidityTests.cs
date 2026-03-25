using FluentResults.Extensions.FluentAssertions;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.ValueObjects;

public class HumidityTests
{
    [Fact]
    public void Create_WhenValidInput_ReturnsHumidity()
    {
        // Act
        var humidityResult = Humidity.FromPercent(50.0);

        // Assert
        using (new AssertionScope())
        {
            humidityResult.Should().BeSuccess();
            humidityResult.Value.Value.Should().Be(50.0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Create_WhenAtBoundary_ReturnsSuccess(double boundaryValue)
    {
        // Act
        var humidityResult = Humidity.FromPercent(boundaryValue);

        // Assert
        humidityResult.Should().BeSuccess();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.1)]
    [InlineData(-100)]
    public void Create_WhenBelowZero_ReturnsFailure(double invalidValue)
    {
        // Act
        var humidityResult = Humidity.FromPercent(invalidValue);

        // Assert
        using (new AssertionScope())
        {
            humidityResult.Should().BeFailure();
            humidityResult.Errors.Should().ContainSingle(e => e.Message.Contains("Humidity"));
        }
    }

    [Theory]
    [InlineData(101)]
    [InlineData(100.1)]
    [InlineData(150)]
    public void Create_WhenAboveHundred_ReturnsFailure(double invalidValue)
    {
        // Act
        var humidityResult = Humidity.FromPercent(invalidValue);

        // Assert
        using (new AssertionScope())
        {
            humidityResult.Should().BeFailure();
            humidityResult.Errors.Should().ContainSingle(e => e.Message.Contains("Humidity"));
        }
    }

    [Fact]
    public void ToString_ReturnsFormattedPercentage()
    {
        // Arrange
        var humidity = Humidity.FromPercent(75.5).Value;

        // Act
        var result = humidity.ToString();

        // Assert
        result.Should().Be("75.5%");
    }
}
