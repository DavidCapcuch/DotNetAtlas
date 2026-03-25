using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.ValueObjects;

public class WindSpeedUnitTests
{
    [Fact]
    public void FromKilometersPerHour_WhenMilesPerHour_ReturnsCorrectConversion()
    {
        // Act
        var result = WindSpeedUnit.MilesPerHour.FromKilometersPerHour(100.0);

        // Assert
        result.Should().BeApproximately(62.14, 0.01);
    }

    [Fact]
    public void ToKilometersPerHour_WhenMilesPerHour_ReturnsCorrectConversion()
    {
        // Act
        var result = WindSpeedUnit.MilesPerHour.ToKilometersPerHour(62.14);

        // Assert
        result.Should().BeApproximately(100.0, 0.1);
    }

    [Fact]
    public void ToKilometersPerHour_AndFromKilometersPerHour_AreInverseOperations()
    {
        // Arrange
        const double originalKmh = 80.0;

        // Act - convert to mph and back
        var mph = WindSpeedUnit.MilesPerHour.FromKilometersPerHour(originalKmh);
        var backFromMph = WindSpeedUnit.MilesPerHour.ToKilometersPerHour(mph);

        // Assert
        backFromMph.Should().BeApproximately(originalKmh, 0.001);
    }

    [Fact]
    public void FormatFromKilometersPerHour_ReturnsFormattedStringWithSymbol()
    {
        // Act
        var kmhFormatted = WindSpeedUnit.KilometersPerHour.FormatFromKilometersPerHour(80.0);
        var mphFormatted = WindSpeedUnit.MilesPerHour.FormatFromKilometersPerHour(80.0);

        // Assert
        using (new AssertionScope())
        {
            kmhFormatted.Should().Be("80.0 km/h");
            mphFormatted.Should().Be("49.7 mph");
        }
    }

    [Fact]
    public void FormatFromKilometersPerHour_WithCustomDecimals_ReturnsCorrectPrecision()
    {
        // Act
        var formatted = WindSpeedUnit.KilometersPerHour.FormatFromKilometersPerHour(80.123, decimals: 2);

        // Assert
        formatted.Should().Be("80.12 km/h");
    }
}
