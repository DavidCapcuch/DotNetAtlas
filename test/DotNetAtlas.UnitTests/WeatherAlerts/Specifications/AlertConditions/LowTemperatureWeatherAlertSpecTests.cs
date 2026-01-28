using DotNetAtlas.Domain.Alerts.Specifications.AlertConditions;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Specifications.AlertConditions;

public class LowTemperatureWeatherAlertSpecTests
{
    private readonly AlertThresholds _thresholds = AlertThresholds.CreateDefault(); // LowTemp = -10°C

    [Theory]
    [InlineData(-11.0)] // Just below threshold
    [InlineData(-20.0)] // Well below threshold
    [InlineData(-40.0)] // Extreme cold
    public void IsSatisfiedBy_WhenTemperatureBelowThreshold_ReturnsTrue(double temperatureC)
    {
        // Arrange
        var spec = new LowTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(temperatureC);

        // Act
        var result = spec.IsSatisfiedBy(reading);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(-10.0)] // At threshold (not below)
    [InlineData(-9.0)] // Above threshold
    [InlineData(20.0)] // Well above threshold
    public void IsSatisfiedBy_WhenTemperatureAtOrAboveThreshold_ReturnsFalse(double temperatureC)
    {
        // Arrange
        var spec = new LowTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(temperatureC);

        // Act
        var result = spec.IsSatisfiedBy(reading);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CreateAlert_ReturnsCorrectAlertType()
    {
        // Arrange
        var spec = new LowTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(-15.0);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Type.Should().Be(AlertType.LowTemperature);
        }
    }

    [Theory]
    [InlineData(-11.0)] // 1°C below threshold
    [InlineData(-14.0)] // 4°C below threshold
    [InlineData(-15.0)] // 5°C below threshold (boundary - still Warning)
    public void CreateAlert_WhenWithinCriticalDifference_ReturnsWarningSeverity(double temperatureC)
    {
        // Arrange
        var spec = new LowTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(temperatureC);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Severity.Should().Be(AlertSeverity.Warning);
        }
    }

    [Theory]
    [InlineData(-15.1)] // >5°C below threshold
    [InlineData(-20.0)] // 10°C below threshold
    [InlineData(-30.0)] // 20°C below threshold
    public void CreateAlert_WhenExceedsCriticalDifference_ReturnsCriticalSeverity(double temperatureC)
    {
        // Arrange
        var spec = new LowTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(temperatureC);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Severity.Should().Be(AlertSeverity.Critical);
        }
    }

    [Fact]
    public void CreateAlert_MessageContainsAlertTypeAndTemperatureInfo()
    {
        // Arrange
        var spec = new LowTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(-18.5);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Message.Should().Contain("Low temperature");
            alertResult.Value.Message.Should().Contain("threshold");
            alertResult.Value.Message.Should().Contain("°C");
        }
    }

    private static WeatherReading CreateReading(double temperatureC)
    {
        return WeatherReading.Create(
            Temperature.FromCelsius(temperatureC).Value,
            Humidity.FromPercent(50.0).Value,
            WindSpeed.FromKilometersPerHour(10.0).Value,
            DateTimeOffset.UtcNow);
    }
}
