using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.ValueObjects;

public class TemperatureUnitTests
{
    [Fact]
    public void FromCelsius_WhenFahrenheit_ReturnsCorrectConversion()
    {
        // Act
        var result = TemperatureUnit.Fahrenheit.FromCelsius(0.0);

        // Assert
        result.Should().Be(32.0);
    }

    [Fact]
    public void FromCelsius_WhenKelvin_ReturnsCorrectConversion()
    {
        // Act
        var result = TemperatureUnit.Kelvin.FromCelsius(0.0);

        // Assert
        result.Should().Be(273.15);
    }

    [Fact]
    public void ToCelsius_WhenFahrenheit_ReturnsCorrectConversion()
    {
        // Act
        var result = TemperatureUnit.Fahrenheit.ToCelsius(32.0);

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public void ToCelsius_WhenKelvin_ReturnsCorrectConversion()
    {
        // Act
        var result = TemperatureUnit.Kelvin.ToCelsius(273.15);

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public void ToCelsius_AndFromCelsius_AreInverseOperations()
    {
        // Arrange
        const double originalCelsius = 25.0;

        // Act - convert to other units and back
        var fahrenheit = TemperatureUnit.Fahrenheit.FromCelsius(originalCelsius);
        var backFromFahrenheit = TemperatureUnit.Fahrenheit.ToCelsius(fahrenheit);

        var kelvin = TemperatureUnit.Kelvin.FromCelsius(originalCelsius);
        var backFromKelvin = TemperatureUnit.Kelvin.ToCelsius(kelvin);

        // Assert
        using (new AssertionScope())
        {
            backFromFahrenheit.Should().BeApproximately(originalCelsius, 0.001);
            backFromKelvin.Should().BeApproximately(originalCelsius, 0.001);
        }
    }

    [Fact]
    public void FormatFromCelsius_ReturnsFormattedStringWithSymbol()
    {
        // Act
        var celsiusFormatted = TemperatureUnit.Celsius.FormatFromCelsius(25.0);
        var fahrenheitFormatted = TemperatureUnit.Fahrenheit.FormatFromCelsius(25.0);
        var kelvinFormatted = TemperatureUnit.Kelvin.FormatFromCelsius(25.0);

        using (new AssertionScope())
        {
            celsiusFormatted.Should().Be("25.0°C");
            fahrenheitFormatted.Should().Be("77.0°F");
            kelvinFormatted.Should().Be("298.1K");
        }
    }

    [Fact]
    public void FormatFromCelsius_WithCustomDecimals_ReturnsCorrectPrecision()
    {
        // Act
        var formatted = TemperatureUnit.Celsius.FormatFromCelsius(25.123, decimals: 2);

        // Assert
        formatted.Should().Be("25.12°C");
    }

    [Fact]
    public void FromCelsius_WhenFahrenheit_WithHighTemperature_ReturnsCorrectConversion()
    {
        // This test exposes the integer division bug (9/5=1 vs 9.0/5.0=1.8)
        // Act
        var result = TemperatureUnit.Fahrenheit.FromCelsius(100.0);

        // Assert
        result.Should().Be(212.0);
    }

    [Fact]
    public void ConvertTo_FahrenheitToKelvin_ReturnsCorrectConversion()
    {
        // Act
        var result = TemperatureUnit.Fahrenheit.ConvertTo(32.0, TemperatureUnit.Kelvin);

        // Assert
        result.Should().Be(273.15);
    }

    [Fact]
    public void ConvertTo_KelvinToFahrenheit_ReturnsCorrectConversion()
    {
        // Act
        var result = TemperatureUnit.Kelvin.ConvertTo(273.15, TemperatureUnit.Fahrenheit);

        // Assert
        result.Should().BeApproximately(32.0, 0.001);
    }

    [Fact]
    public void ConvertTo_SameUnit_ReturnsOriginalValue()
    {
        // Act
        var result = TemperatureUnit.Celsius.ConvertTo(25.0, TemperatureUnit.Celsius);

        // Assert
        result.Should().Be(25.0);
    }

    [Fact]
    public void ConvertTo_RoundTrip_MaintainsPrecision()
    {
        // Arrange
        const double originalFahrenheit = 98.6;  // Human body temperature

        // Act - convert F → K → F
        var kelvin = TemperatureUnit.Fahrenheit.ConvertTo(originalFahrenheit, TemperatureUnit.Kelvin);
        var backToFahrenheit = TemperatureUnit.Kelvin.ConvertTo(kelvin, TemperatureUnit.Fahrenheit);

        // Assert
        backToFahrenheit.Should().BeApproximately(originalFahrenheit, 0.001);
    }

    [Fact]
    public void ConvertTo_FahrenheitToCelsius_ReturnsCorrectConversion()
    {
        // Act
        var result = TemperatureUnit.Fahrenheit.ConvertTo(212.0, TemperatureUnit.Celsius);

        // Assert
        result.Should().Be(100.0);
    }

    [Fact]
    public void ConvertTo_KelvinToCelsius_ReturnsCorrectConversion()
    {
        // Act
        var result = TemperatureUnit.Kelvin.ConvertTo(373.15, TemperatureUnit.Celsius);

        // Assert
        result.Should().BeApproximately(100.0, 0.001);
    }
}
