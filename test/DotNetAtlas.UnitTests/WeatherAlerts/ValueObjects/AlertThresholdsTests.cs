using DotNetAtlas.Domain.Alerts.ValueObjects;

namespace DotNetAtlas.UnitTests.WeatherAlerts.ValueObjects;

public class AlertThresholdsTests
{
    [Fact]
    public void Create_WhenValidInput_ReturnsThresholds()
    {
        // Arrange
        var highTemp = Temperature.FromCelsius(35.0).Value;
        var lowTemp = Temperature.FromCelsius(-10.0).Value;
        var highWind = WindSpeed.FromKilometersPerHour(80.0).Value;
        var highHumidity = Humidity.FromPercent(90.0).Value;
        var lowHumidity = Humidity.FromPercent(20.0).Value;

        // Act
        var result = AlertThresholds.Create(highTemp, lowTemp, highWind, highHumidity, lowHumidity);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.HighTemperature.In(TemperatureUnit.Celsius).Should().Be(35.0);
            result.Value.LowTemperature.In(TemperatureUnit.Celsius).Should().Be(-10.0);
            result.Value.HighWindSpeed.In(WindSpeedUnit.KilometersPerHour).Should().Be(80.0);
            result.Value.HighHumidity.Value.Should().Be(90.0);
            result.Value.LowHumidity.Value.Should().Be(20.0);
        }
    }

    [Fact]
    public void CreateDefault_ReturnsDefaultThresholds()
    {
        // Act
        var alertThresholds = AlertThresholds.CreateDefault();

        // Assert
        using (new AssertionScope())
        {
            alertThresholds.HighTemperature.In(TemperatureUnit.Celsius).Should().Be(35.0);
            alertThresholds.LowTemperature.In(TemperatureUnit.Celsius).Should().Be(-10.0);
            alertThresholds.HighWindSpeed.In(WindSpeedUnit.KilometersPerHour).Should().Be(80.0);
            alertThresholds.HighHumidity.Value.Should().Be(90.0);
            alertThresholds.LowHumidity.Value.Should().Be(20.0);
        }
    }

    [Fact]
    public void Create_WithCustomValues_ReturnsCorrectThresholds()
    {
        // Arrange
        var highTemp = Temperature.FromCelsius(40.0).Value;
        var lowTemp = Temperature.FromCelsius(-20.0).Value;
        var highWind = WindSpeed.FromKilometersPerHour(100.0).Value;
        var highHumidity = Humidity.FromPercent(95.0).Value;
        var lowHumidity = Humidity.FromPercent(15.0).Value;

        // Act
        var result = AlertThresholds.Create(highTemp, lowTemp, highWind, highHumidity, lowHumidity);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.HighTemperature.In(TemperatureUnit.Celsius).Should().Be(40.0);
            result.Value.LowTemperature.In(TemperatureUnit.Celsius).Should().Be(-20.0);
            result.Value.HighWindSpeed.In(WindSpeedUnit.KilometersPerHour).Should().Be(100.0);
        }
    }

    [Fact]
    public void Create_WhenLowTemperatureGreaterThanOrEqualToHigh_ReturnsFailed()
    {
        // Arrange
        var highTemp = Temperature.FromCelsius(20.0).Value;
        var lowTemp = Temperature.FromCelsius(25.0).Value; // Invalid: low > high
        var highWind = WindSpeed.FromKilometersPerHour(80.0).Value;
        var highHumidity = Humidity.FromPercent(90.0).Value;
        var lowHumidity = Humidity.FromPercent(20.0).Value;

        // Act
        var result = AlertThresholds.Create(highTemp, lowTemp, highWind, highHumidity, lowHumidity);

        // Assert
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Create_WhenLowHumidityGreaterThanOrEqualToHigh_ReturnsFailed()
    {
        // Arrange
        var highTemp = Temperature.FromCelsius(35.0).Value;
        var lowTemp = Temperature.FromCelsius(-10.0).Value;
        var highWind = WindSpeed.FromKilometersPerHour(80.0).Value;
        var highHumidity = Humidity.FromPercent(50.0).Value;
        var lowHumidity = Humidity.FromPercent(60.0).Value; // Invalid: low > high

        // Act
        var result = AlertThresholds.Create(highTemp, lowTemp, highWind, highHumidity, lowHumidity);

        // Assert
        result.IsFailed.Should().BeTrue();
    }
}
