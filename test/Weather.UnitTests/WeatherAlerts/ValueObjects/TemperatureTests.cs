using FluentResults;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.ValueObjects;

public class TemperatureTests
{
    [Fact]
    public void FromCelsius_WhenValidInput_ReturnsCorrectTemperature()
    {
        // Act
        var result = Temperature.FromCelsius(25.0);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            var temperature = result.Value;
            temperature.In(TemperatureUnit.Celsius).Should().Be(25.0);
            temperature.In(TemperatureUnit.Fahrenheit).Should().Be(77.0);
            temperature.In(TemperatureUnit.Kelvin).Should().Be(298.15);
        }
    }

    [Fact]
    public void FromFahrenheit_WhenValidInput_ReturnsCorrectTemperature()
    {
        // Act
        var result = Temperature.FromFahrenheit(77.0);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            var temperature = result.Value;
            temperature.In(TemperatureUnit.Celsius).Should().Be(25.0);
            temperature.In(TemperatureUnit.Fahrenheit).Should().Be(77.0);
        }
    }

    [Fact]
    public void FromKelvin_WhenValidInput_ReturnsCorrectTemperature()
    {
        // Act
        var result = Temperature.FromKelvin(298.15);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            var temperature = result.Value;
            temperature.In(TemperatureUnit.Celsius).Should().Be(25.0);
            temperature.In(TemperatureUnit.Kelvin).Should().Be(298.15);
        }
    }

    [Fact]
    public void FromCelsius_WhenZero_ReturnsCorrectConversions()
    {
        // Act
        var result = Temperature.FromCelsius(0.0);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            var temperature = result.Value;
            temperature.In(TemperatureUnit.Celsius).Should().Be(0.0);
            temperature.In(TemperatureUnit.Fahrenheit).Should().Be(32.0);
            temperature.In(TemperatureUnit.Kelvin).Should().Be(273.15);
        }
    }

    [Fact]
    public void FromCelsius_WhenNegative_ReturnsCorrectConversions()
    {
        // Act
        var result = Temperature.FromCelsius(-40.0);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            var temperature = result.Value;
            temperature.In(TemperatureUnit.Celsius).Should().Be(-40.0);
            temperature.In(TemperatureUnit.Fahrenheit).Should().Be(-40.0); // -40 is where C and F are equal
            temperature.In(TemperatureUnit.Kelvin).Should().BeApproximately(233.15, 0.001);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void In_ReturnsCorrectValueForUnit(int unitValue)
    {
        // Arrange
        var result = Temperature.FromCelsius(100.0);
        result.IsSuccess.Should().BeTrue();
        var temperature = result.Value;
        var unit = TemperatureUnit.FromValue(unitValue);

        // Act
        var valueInUnit = temperature.In(unit);

        // Assert
        if (unit == TemperatureUnit.Celsius)
        {
            valueInUnit.Should().Be(100.0);
        }
        else if (unit == TemperatureUnit.Fahrenheit)
        {
            valueInUnit.Should().Be(212.0);
        }
        else
        {
            valueInUnit.Should().Be(373.15);
        }
    }

    [Fact]
    public void Format_ReturnsFormattedString()
    {
        // Arrange
        var result = Temperature.FromCelsius(25.5);
        result.IsSuccess.Should().BeTrue();
        var temperature = result.Value;

        // Act
        var formattedCelsius = temperature.Format(TemperatureUnit.Celsius);
        var formattedFahrenheit = temperature.Format(TemperatureUnit.Fahrenheit);
        var formattedKelvin = temperature.Format(TemperatureUnit.Kelvin);

        // Assert - 25.5 + 273.15 = 298.65, rounds to 298.6 with F1 format
        using (new AssertionScope())
        {
            formattedCelsius.Should().Be("25.5°C");
            formattedFahrenheit.Should().Be("77.9°F");
            formattedKelvin.Should().Be("298.6K");
        }
    }

    [Fact]
    public void ToString_ReturnsFormattedCelsius()
    {
        // Arrange
        var result = Temperature.FromCelsius(25.0);
        result.IsSuccess.Should().BeTrue();
        var temperature = result.Value;

        // Act
        var resultString = temperature.ToString();

        // Assert
        resultString.Should().Be("25.0°C");
    }

    [Theory]
    [InlineData(-274, "°C")] // Below absolute zero in Celsius
    [InlineData(-460, "°F")] // Below absolute zero in Fahrenheit
    [InlineData(-1, "K")] // Below absolute zero in Kelvin
    public void FromTemperature_WhenBelowAbsoluteZero_ReturnsFailure(double value, string unit)
    {
        // Act
        Result<Temperature> result = unit == "°C"
            ? Temperature.FromCelsius(value)
            : unit == "°F"
                ? Temperature.FromFahrenheit(value)
                : Temperature.FromKelvin(value);

        // Assert
        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().ContainSingle();
            result.Errors[0].Message.Should().Contain("cannot be below absolute zero");
        }
    }
}
