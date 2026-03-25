using FluentResults.Extensions.FluentAssertions;
using Weather.Domain.Alerts.Specifications.AlertConditions;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.Specifications.AlertConditions;

public class HighHumidityWeatherAlertSpecTests
{
    private readonly AlertThresholds _thresholds = AlertThresholds.CreateDefault(); // HighHumidity = 90%

    [Theory]
    [InlineData(91.0)] // Just above threshold
    [InlineData(95.0)] // Well above threshold
    [InlineData(100.0)] // Maximum humidity
    public void IsSatisfiedBy_WhenHumidityAboveThreshold_ReturnsTrue(double humidityPercent)
    {
        // Arrange
        var spec = new HighHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(humidityPercent);

        // Act
        var result = spec.IsSatisfiedBy(reading);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(90.0)] // At threshold (not above)
    [InlineData(89.0)] // Below threshold
    [InlineData(50.0)] // Normal humidity
    public void IsSatisfiedBy_WhenHumidityAtOrBelowThreshold_ReturnsFalse(double humidityPercent)
    {
        // Arrange
        var spec = new HighHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(humidityPercent);

        // Act
        var result = spec.IsSatisfiedBy(reading);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CreateAlert_ReturnsCorrectAlertType()
    {
        // Arrange
        var spec = new HighHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(95.0);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Type.Should().Be(AlertType.HighHumidity);
        }
    }

    [Theory]
    [InlineData(91.0)] // 1% above threshold
    [InlineData(94.0)] // 4% above threshold
    [InlineData(95.0)] // 5% above threshold (boundary - still Warning)
    public void CreateAlert_WhenWithinCriticalDifference_ReturnsWarningSeverity(double humidityPercent)
    {
        // Arrange
        var spec = new HighHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(humidityPercent);

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
    [InlineData(95.1)] // >5% above threshold
    [InlineData(98.0)] // 8% above threshold
    [InlineData(100.0)] // 10% above threshold
    public void CreateAlert_WhenExceedsCriticalDifference_ReturnsCriticalSeverity(double humidityPercent)
    {
        // Arrange
        var spec = new HighHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(humidityPercent);

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
    public void CreateAlert_MessageContainsAlertTypeAndHumidityInfo()
    {
        // Arrange
        var spec = new HighHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(97.5);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Message.Should().Contain("High humidity");
        }
    }

    private static WeatherReading CreateReading(double humidityPercent)
    {
        return WeatherReading.Create(
            Temperature.FromCelsius(25.0).Value,
            Humidity.FromPercent(humidityPercent).Value,
            WindSpeed.FromKilometersPerHour(10.0).Value,
            DateTimeOffset.UtcNow);
    }
}
