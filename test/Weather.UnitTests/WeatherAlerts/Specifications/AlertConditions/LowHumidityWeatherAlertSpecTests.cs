using FluentResults.Extensions.FluentAssertions;
using Weather.Domain.Alerts.Specifications.AlertConditions;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.Specifications.AlertConditions;

public class LowHumidityWeatherAlertSpecTests
{
    private readonly AlertThresholds _thresholds = AlertThresholds.CreateDefault(); // LowHumidity = 20%

    [Theory]
    [InlineData(19.0)] // Just below threshold
    [InlineData(10.0)] // Well below threshold
    [InlineData(0.0)] // Minimum humidity
    public void IsSatisfiedBy_WhenHumidityBelowThreshold_ReturnsTrue(double humidityPercent)
    {
        // Arrange
        var spec = new LowHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(humidityPercent);

        // Act
        var result = spec.IsSatisfiedBy(reading);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(20.0)] // At threshold (not below)
    [InlineData(21.0)] // Above threshold
    [InlineData(50.0)] // Normal humidity
    public void IsSatisfiedBy_WhenHumidityAtOrAboveThreshold_ReturnsFalse(double humidityPercent)
    {
        // Arrange
        var spec = new LowHumidityWeatherAlertSpec(_thresholds);
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
        var spec = new LowHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(10.0);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Type.Should().Be(AlertType.LowHumidity);
        }
    }

    [Theory]
    [InlineData(19.0)] // 1% below threshold
    [InlineData(16.0)] // 4% below threshold
    [InlineData(15.0)] // 5% below threshold (boundary - still Warning)
    public void CreateAlert_WhenWithinCriticalDifference_ReturnsWarningSeverity(double humidityPercent)
    {
        // Arrange
        var spec = new LowHumidityWeatherAlertSpec(_thresholds);
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
    [InlineData(14.9)] // >5% below threshold
    [InlineData(10.0)] // 10% below threshold
    [InlineData(0.0)] // 20% below threshold
    public void CreateAlert_WhenExceedsCriticalDifference_ReturnsCriticalSeverity(double humidityPercent)
    {
        // Arrange
        var spec = new LowHumidityWeatherAlertSpec(_thresholds);
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
        var spec = new LowHumidityWeatherAlertSpec(_thresholds);
        var reading = CreateReading(12.5);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Message.Should().Contain("Low humidity");
            alertResult.Value.Message.Should().Contain("threshold");
            alertResult.Value.Message.Should().Contain("%");
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
