using DotNetAtlas.Domain.Alerts.Specifications.AlertConditions;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Specifications.AlertConditions;

public class HighWindWeatherAlertSpecTests
{
    private readonly AlertThresholds _thresholds = AlertThresholds.CreateDefault(); // HighWind = 80 km/h

    [Theory]
    [InlineData(81.0)] // Just above threshold
    [InlineData(100.0)] // Well above threshold
    [InlineData(150.0)] // Storm-level
    public void IsSatisfiedBy_WhenWindSpeedAboveThreshold_ReturnsTrue(double windSpeedKmh)
    {
        // Arrange
        var spec = new HighWindWeatherAlertSpec(_thresholds);
        var reading = CreateReading(windSpeedKmh);

        // Act
        var result = spec.IsSatisfiedBy(reading);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(80.0)] // At threshold (not above)
    [InlineData(79.0)] // Below threshold
    [InlineData(10.0)] // Calm wind
    public void IsSatisfiedBy_WhenWindSpeedAtOrBelowThreshold_ReturnsFalse(double windSpeedKmh)
    {
        // Arrange
        var spec = new HighWindWeatherAlertSpec(_thresholds);
        var reading = CreateReading(windSpeedKmh);

        // Act
        var result = spec.IsSatisfiedBy(reading);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CreateAlert_ReturnsCorrectAlertType()
    {
        // Arrange
        var spec = new HighWindWeatherAlertSpec(_thresholds);
        var reading = CreateReading(90.0);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Type.Should().Be(AlertType.HighWind);
        }
    }

    [Theory]
    [InlineData(81.0)] // 1 km/h above threshold
    [InlineData(95.0)] // 15 km/h above threshold
    [InlineData(100.0)] // 20 km/h above threshold (boundary - still Warning)
    public void CreateAlert_WhenWithinCriticalDifference_ReturnsWarningSeverity(double windSpeedKmh)
    {
        // Arrange
        var spec = new HighWindWeatherAlertSpec(_thresholds);
        var reading = CreateReading(windSpeedKmh);

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
    [InlineData(100.1)] // >20 km/h above threshold
    [InlineData(120.0)] // 40 km/h above threshold
    [InlineData(150.0)] // 70 km/h above threshold
    public void CreateAlert_WhenExceedsCriticalDifference_ReturnsCriticalSeverity(double windSpeedKmh)
    {
        // Arrange
        var spec = new HighWindWeatherAlertSpec(_thresholds);
        var reading = CreateReading(windSpeedKmh);

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
    public void CreateAlert_MessageContainsAlertTypeAndWindInfo()
    {
        // Arrange
        var spec = new HighWindWeatherAlertSpec(_thresholds);
        var reading = CreateReading(95.5);

        // Act
        var alertResult = spec.CreateAlert(reading);

        // Assert
        using (new AssertionScope())
        {
            alertResult.Should().BeSuccess();
            alertResult.Value.Message.Should().Contain("High wind");
            alertResult.Value.Message.Should().Contain("threshold");
            alertResult.Value.Message.Should().Contain("km/h");
        }
    }

    private static WeatherReading CreateReading(double windSpeedKmh)
    {
        return WeatherReading.Create(
            Temperature.FromCelsius(20.0).Value,
            Humidity.FromPercent(50.0).Value,
            WindSpeed.FromKilometersPerHour(windSpeedKmh).Value,
            DateTimeOffset.UtcNow);
    }
}
