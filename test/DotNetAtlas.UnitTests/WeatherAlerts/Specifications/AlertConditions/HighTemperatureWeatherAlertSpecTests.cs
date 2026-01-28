using DotNetAtlas.Domain.Alerts.Specifications.AlertConditions;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Specifications.AlertConditions;

public class HighTemperatureWeatherAlertSpecTests
{
    private readonly AlertThresholds _thresholds = AlertThresholds.CreateDefault(); // HighTemp = 35°C

    [Theory]
    [InlineData(36.0)] // Just above threshold
    [InlineData(40.0)] // Well above threshold
    [InlineData(50.0)] // Extreme
    public void IsSatisfiedBy_WhenTemperatureAboveThreshold_ReturnsTrue(double temperatureC)
    {
        // Arrange
        var spec = new HighTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(temperatureC);

        // Act
        var result = spec.IsSatisfiedBy(reading);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(35.0)] // At threshold (not above)
    [InlineData(34.0)] // Below threshold
    [InlineData(20.0)] // Well below threshold
    public void IsSatisfiedBy_WhenTemperatureAtOrBelowThreshold_ReturnsFalse(double temperatureC)
    {
        // Arrange
        var spec = new HighTemperatureWeatherAlertSpec(_thresholds);
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
        var spec = new HighTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(40.0);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Type.Should().Be(AlertType.HighTemperature);
        }
    }

    [Theory]
    [InlineData(36.0)] // 1°C above threshold
    [InlineData(39.0)] // 4°C above threshold
    [InlineData(40.0)] // 5°C above threshold (boundary - still Warning)
    public void CreateAlert_WhenWithinCriticalDifference_ReturnsWarningSeverity(double temperatureC)
    {
        // Arrange
        var spec = new HighTemperatureWeatherAlertSpec(_thresholds);
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
    [InlineData(40.1)] // >5°C above threshold
    [InlineData(45.0)] // 10°C above threshold
    [InlineData(50.0)] // 15°C above threshold
    public void CreateAlert_WhenExceedsCriticalDifference_ReturnsCriticalSeverity(double temperatureC)
    {
        // Arrange
        var spec = new HighTemperatureWeatherAlertSpec(_thresholds);
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
        var spec = new HighTemperatureWeatherAlertSpec(_thresholds);
        var reading = CreateReading(42.5);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Message.Should().Contain("High temperature");
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
