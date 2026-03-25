using FluentResults.Extensions.FluentAssertions;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.ValueObjects;

public class WindSpeedTests
{
    [Fact]
    public void FromKilometersPerHour_WhenValidInput_ReturnsWindSpeed()
    {
        // Act
        var windSpeedResult = WindSpeed.FromKilometersPerHour(80.0);

        // Assert
        using (new AssertionScope())
        {
            windSpeedResult.Should().BeSuccess();
            windSpeedResult.Value.In(WindSpeedUnit.KilometersPerHour).Should().Be(80.0);
            windSpeedResult.Value.In(WindSpeedUnit.MilesPerHour).Should().BeApproximately(49.71, 0.01);
        }
    }

    [Fact]
    public void FromMilesPerHour_WhenValidInput_ReturnsWindSpeed()
    {
        // Act
        var windSpeedResult = WindSpeed.FromMilesPerHour(50.0);

        // Assert
        using (new AssertionScope())
        {
            windSpeedResult.Should().BeSuccess();
            windSpeedResult.Value.In(WindSpeedUnit.MilesPerHour).Should().BeApproximately(50.0, 0.01);
            windSpeedResult.Value.In(WindSpeedUnit.KilometersPerHour).Should().BeApproximately(80.47, 0.01);
        }
    }

    [Fact]
    public void FromKilometersPerHour_WhenNegative_ReturnsFailure()
    {
        // Act
        var windSpeedResult = WindSpeed.FromKilometersPerHour(-10.0);

        // Assert
        using (new AssertionScope())
        {
            windSpeedResult.Should().BeFailure();
            windSpeedResult.Errors.Should().ContainSingle(e => e.Message.Contains("Wind speed"));
        }
    }

    [Fact]
    public void FromMilesPerHour_WhenNegative_ReturnsFailure()
    {
        // Act
        var windSpeedResult = WindSpeed.FromMilesPerHour(-10.0);

        // Assert
        using (new AssertionScope())
        {
            windSpeedResult.Should().BeFailure();
            windSpeedResult.Errors.Should().ContainSingle(e => e.Message.Contains("Wind speed"));
        }
    }

    [Fact]
    public void FromKilometersPerHour_WhenZero_ReturnsSuccess()
    {
        // Act
        var windSpeedResult = WindSpeed.FromKilometersPerHour(0);

        // Assert
        windSpeedResult.Should().BeSuccess();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void In_ReturnsCorrectValueForUnit(int unitValue)
    {
        // Arrange
        var windSpeed = WindSpeed.FromKilometersPerHour(100.0).Value;
        var unit = WindSpeedUnit.FromValue(unitValue);

        // Act
        var valueInUnit = windSpeed.In(unit);

        // Assert
        if (unit == WindSpeedUnit.KilometersPerHour)
        {
            valueInUnit.Should().Be(100.0);
        }
        else
        {
            valueInUnit.Should().BeApproximately(62.14, 0.01);
        }
    }

    [Fact]
    public void Format_ReturnsFormattedString()
    {
        // Arrange
        var windSpeed = WindSpeed.FromKilometersPerHour(80.0).Value;

        // Act
        var formattedKmh = windSpeed.Format(WindSpeedUnit.KilometersPerHour);
        var formattedMph = windSpeed.Format(WindSpeedUnit.MilesPerHour);

        // Assert
        using (new AssertionScope())
        {
            formattedKmh.Should().Be("80.0 km/h");
            formattedMph.Should().Be("49.7 mph");
        }
    }

    [Fact]
    public void ToString_ReturnsFormattedKilometersPerHour()
    {
        // Arrange
        var windSpeed = WindSpeed.FromKilometersPerHour(80.0).Value;

        // Act
        var result = windSpeed.ToString();

        // Assert
        result.Should().Be("80.0 km/h");
    }
}
