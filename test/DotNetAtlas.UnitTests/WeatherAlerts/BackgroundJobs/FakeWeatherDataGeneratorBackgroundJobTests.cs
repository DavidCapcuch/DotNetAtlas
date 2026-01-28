using DotNetAtlas.Domain.Alerts.Specifications.AlertConditions;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Infrastructure.BackgroundJobs.WeatherAlerts;

namespace DotNetAtlas.UnitTests.WeatherAlerts.BackgroundJobs;

public class FakeWeatherDataGeneratorBackgroundJobTests
{
    private readonly AlertThresholds _defaultThresholds = AlertThresholds.CreateDefault();

    [Fact]
    public void GenerateAlertTriggeringReading_ReturnsReadingThatExceedsHighTemperatureThreshold()
    {
        // Act
        var reading = FakeWeatherDataGeneratorBackgroundJob.GenerateAlertTriggeringReading();

        // Assert
        using (new AssertionScope())
        {
            reading.TemperatureC.Should().BeGreaterThan(_defaultThresholds.HighTemperature.In(TemperatureUnit.Celsius));
            reading.HumidityPercent.Should().BeInRange(0, 100);
            reading.WindSpeedKmh.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void GenerateAlertTriggeringReading_TriggersHighTemperatureAlert()
    {
        // Arrange
        var reading = FakeWeatherDataGeneratorBackgroundJob.GenerateAlertTriggeringReading();
        var weatherReading = WeatherReading.Create(
            Temperature.FromCelsius(reading.TemperatureC).Value,
            Humidity.FromPercent(reading.HumidityPercent).Value,
            WindSpeed.FromKilometersPerHour(reading.WindSpeedKmh).Value,
            DateTimeOffset.UtcNow);
        var spec = new HighTemperatureWeatherAlertSpec(_defaultThresholds);

        // Act
        var isSatisfied = spec.IsSatisfiedBy(weatherReading);

        // Assert
        isSatisfied.Should().BeTrue("the generated reading should trigger a high temperature alert");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void GenerateReadingsBatch_FirstReadingAlwaysTriggersAlert(int batchSize)
    {
        // Arrange
        var readingTime = DateTimeOffset.UtcNow;

        // Act
        var readings = FakeWeatherDataGeneratorBackgroundJob.GenerateReadingsBatch(batchSize, readingTime);

        // Assert
        using (new AssertionScope())
        {
            readings.Should().HaveCount(batchSize);

            // First reading should be alert-triggering (40°C)
            var firstReading = readings[0];
            firstReading.TemperatureC.Should().Be(40.0, "first reading should be the alert-triggering temperature");

            // All readings should have the same timestamp
            readings.Should().OnlyContain(r => r.RecordedAtUtc == readingTime);
        }
    }

    [Fact]
    public void GenerateReadingsBatch_FirstReadingTriggersHighTemperatureAlert()
    {
        // Arrange
        var readingTime = DateTimeOffset.UtcNow;
        var readings = FakeWeatherDataGeneratorBackgroundJob.GenerateReadingsBatch(5, readingTime);
        var firstDto = readings[0];
        var weatherReading = WeatherReading.Create(
            Temperature.FromCelsius(firstDto.TemperatureC).Value,
            Humidity.FromPercent(firstDto.HumidityPercent).Value,
            WindSpeed.FromKilometersPerHour(firstDto.WindSpeedKmh).Value,
            firstDto.RecordedAtUtc);
        var spec = new HighTemperatureWeatherAlertSpec(_defaultThresholds);

        // Act
        var isSatisfied = spec.IsSatisfiedBy(weatherReading);

        // Assert
        isSatisfied.Should().BeTrue("the first reading in every batch must trigger an alert for demo purposes");
    }

    [Fact]
    public void GenerateReadingsBatch_WithSingleReading_ReturnsAlertTriggeringReading()
    {
        // Arrange
        var readingTime = DateTimeOffset.UtcNow;

        // Act
        var readings = FakeWeatherDataGeneratorBackgroundJob.GenerateReadingsBatch(1, readingTime);

        // Assert
        using (new AssertionScope())
        {
            readings.Should().HaveCount(1);
            readings[0].TemperatureC.Should().Be(40.0, "single reading batch should contain alert-triggering reading");
        }
    }
}
