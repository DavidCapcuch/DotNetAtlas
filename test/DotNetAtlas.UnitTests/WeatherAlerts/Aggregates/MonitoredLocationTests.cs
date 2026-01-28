using DotNetAtlas.Domain.Alerts;
using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Alerts.Events;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Domain.Common.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Aggregates;

public class MonitoredLocationTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Fact]
    public void CreateWithDefaultThresholds_WhenValidInput_ReturnsMonitoredLocationWithDefaultThresholds()
    {
        // Arrange
        var location = CreateLocation("Prague", CountryCode.CZ);

        // Act
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);

        // Assert
        using (new AssertionScope())
        {
            monitoredLocation.Should().NotBeNull();
            monitoredLocation.Location.Should().Be(location);
            monitoredLocation.IsActive.Should().BeTrue();
            monitoredLocation.Thresholds.Should().NotBeNull();
            monitoredLocation.RecentReadings.Should().BeEmpty();
            monitoredLocation.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<MonitoredLocationCreatedDomainEvent>();
        }
    }

    [Fact]
    public void Create_WithCustomThresholds_ReturnsMonitoredLocationWithCustomThresholds()
    {
        // Arrange
        var location = CreateLocation("Berlin", CountryCode.DE);
        var customThresholds = CreateCustomThresholds(40.0, -20.0, 100.0, 95.0, 15.0);

        // Act
        var monitoredLocation = MonitoredLocation.Create(location, customThresholds);

        // Assert
        using (new AssertionScope())
        {
            monitoredLocation.Thresholds.HighTemperature.In(TemperatureUnit.Celsius).Should().Be(40.0);
            monitoredLocation.Thresholds.LowTemperature.In(TemperatureUnit.Celsius).Should().Be(-20.0);
            monitoredLocation.Thresholds.HighWindSpeed.In(WindSpeedUnit.KilometersPerHour).Should().Be(100.0);
            monitoredLocation.Thresholds.HighHumidity.Value.Should().Be(95.0);
            monitoredLocation.Thresholds.LowHumidity.Value.Should().Be(15.0);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenBelowThresholds_DoesNotRaiseAlert()
    {
        // Arrange
        var location = CreateLocation("Vienna", CountryCode.AT);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        var weatherReading = CreateWeatherReading(25.0, 50.0, 15.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            monitoredLocation.RecentReadings.Should().ContainSingle();
            monitoredLocation.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenHighTemperature_RaisesAlertWithCorrectSeverity()
    {
        // Arrange
        var location = CreateLocation("Madrid", CountryCode.ES);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        // 40°C is above default threshold of 35°C but less than 5°C above = Warning
        var weatherReading = CreateWeatherReading(40.0, 50.0, 15.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            var domainEvents = monitoredLocation.PopDomainEvents();
            domainEvents.Should().ContainSingle();
            var alertEvent = domainEvents[0] as WeatherAlertIssuedDomainEvent;
            alertEvent.Should().NotBeNull();
            alertEvent!.WeatherAlert.Type.Should().Be(AlertType.HighTemperature);
            alertEvent.WeatherAlert.Severity.Should().Be(AlertSeverity.Warning);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenHighTemperatureCritical_RaisesCriticalAlert()
    {
        // Arrange
        var location = CreateLocation("Seville", CountryCode.ES);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        // 42°C is more than 5°C above default threshold of 35°C = Critical
        var weatherReading = CreateWeatherReading(42.0, 50.0, 15.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            var domainEvents = monitoredLocation.PopDomainEvents();
            var alertEvent = domainEvents[0] as WeatherAlertIssuedDomainEvent;
            alertEvent!.WeatherAlert.Severity.Should().Be(AlertSeverity.Critical);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenLowTemperature_RaisesAlert()
    {
        // Arrange
        var location = CreateLocation("Oslo", CountryCode.NO);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        var weatherReading = CreateWeatherReading(-15.0, 50.0, 15.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            var domainEvents = monitoredLocation.PopDomainEvents();
            domainEvents.Should().ContainSingle();
            var alertEvent = domainEvents[0] as WeatherAlertIssuedDomainEvent;
            alertEvent.Should().NotBeNull();
            alertEvent!.WeatherAlert.Type.Should().Be(AlertType.LowTemperature);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenHighWind_RaisesAlert()
    {
        // Arrange
        var location = CreateLocation("Dublin", CountryCode.IE);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        var weatherReading = CreateWeatherReading(20.0, 50.0, 100.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            var domainEvents = monitoredLocation.PopDomainEvents();
            domainEvents.Should().ContainSingle();
            var alertEvent = domainEvents[0] as WeatherAlertIssuedDomainEvent;
            alertEvent.Should().NotBeNull();
            alertEvent!.WeatherAlert.Type.Should().Be(AlertType.HighWind);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenMultipleThresholdsExceeded_RaisesMultipleAlerts()
    {
        // Arrange
        var location = CreateLocation("Rome", CountryCode.IT);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        var weatherReading = CreateWeatherReading(40.0, 50.0, 100.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            var domainEvents = monitoredLocation.PopDomainEvents();
            domainEvents.Should().HaveCount(2);
            domainEvents.Should().Contain(e => ((WeatherAlertIssuedDomainEvent)e).WeatherAlert.Type == AlertType.HighTemperature);
            domainEvents.Should().Contain(e => ((WeatherAlertIssuedDomainEvent)e).WeatherAlert.Type == AlertType.HighWind);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenDeactivated_DoesNotRaiseAlert()
    {
        // Arrange
        var location = CreateLocation("Paris", CountryCode.FR);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event
        monitoredLocation.DeactivateMonitoring();

        var weatherReading = CreateWeatherReading(40.0, 50.0, 15.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            monitoredLocation.RecentReadings.Should().ContainSingle();
            monitoredLocation.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Activate_WhenDeactivated_SetsIsActiveToTrue()
    {
        // Arrange
        var location = CreateLocation("London", CountryCode.GB);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        monitoredLocation.DeactivateMonitoring();

        // Act
        monitoredLocation.ActivateMonitoring();

        // Assert
        monitoredLocation.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_WhenActive_SetsIsActiveToFalse()
    {
        // Arrange
        var location = CreateLocation("Amsterdam", CountryCode.NL);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);

        // Act
        monitoredLocation.DeactivateMonitoring();

        // Assert
        monitoredLocation.IsActive.Should().BeFalse();
    }

    [Fact]
    public void UpdateThresholds_ReplacesExistingThresholds()
    {
        // Arrange
        var location = CreateLocation("Brussels", CountryCode.BE);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        var newThresholds = CreateCustomThresholds(45.0, -25.0, 120.0, 95.0, 10.0);

        // Act
        monitoredLocation.UpdateThresholds(newThresholds);

        // Assert
        using (new AssertionScope())
        {
            monitoredLocation.Thresholds.HighTemperature.In(TemperatureUnit.Celsius).Should().Be(45.0);
            monitoredLocation.Thresholds.LowTemperature.In(TemperatureUnit.Celsius).Should().Be(-25.0);
            monitoredLocation.Thresholds.HighWindSpeed.In(WindSpeedUnit.KilometersPerHour).Should().Be(120.0);
            monitoredLocation.Thresholds.HighHumidity.Value.Should().Be(95.0);
            monitoredLocation.Thresholds.LowHumidity.Value.Should().Be(10.0);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenHighHumidity_RaisesAlert()
    {
        // Arrange
        var location = CreateLocation("Singapore", CountryCode.SG);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        // 95% is above default threshold of 90%
        var weatherReading = CreateWeatherReading(25.0, 95.0, 10.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            var domainEvents = monitoredLocation.PopDomainEvents();
            domainEvents.Should().ContainSingle();
            var alertEvent = domainEvents[0] as WeatherAlertIssuedDomainEvent;
            alertEvent.Should().NotBeNull();
            alertEvent!.WeatherAlert.Type.Should().Be(AlertType.HighHumidity);
            alertEvent.WeatherAlert.Severity.Should().Be(AlertSeverity.Warning);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenLowHumidity_RaisesAlert()
    {
        // Arrange
        var location = CreateLocation("Phoenix", CountryCode.US);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        // 15% is below default threshold of 20%
        var weatherReading = CreateWeatherReading(30.0, 15.0, 10.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            var domainEvents = monitoredLocation.PopDomainEvents();
            domainEvents.Should().ContainSingle();
            var alertEvent = domainEvents[0] as WeatherAlertIssuedDomainEvent;
            alertEvent.Should().NotBeNull();
            alertEvent!.WeatherAlert.Type.Should().Be(AlertType.LowHumidity);
        }
    }

    [Fact]
    public void RecordWeatherReading_WhenHighHumidityCritical_RaisesCriticalAlert()
    {
        // Arrange
        var location = CreateLocation("Mumbai", CountryCode.IN);
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        _ = monitoredLocation.PopDomainEvents(); // Clear creation event

        // 98% is more than 5% above default threshold of 90% = Critical
        var weatherReading = CreateWeatherReading(30.0, 98.0, 10.0);

        // Act
        monitoredLocation.RecordWeatherReading(weatherReading, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            var domainEvents = monitoredLocation.PopDomainEvents();
            var alertEvent = domainEvents[0] as WeatherAlertIssuedDomainEvent;
            alertEvent!.WeatherAlert.Severity.Should().Be(AlertSeverity.Critical);
        }
    }

    private static Location CreateLocation(string city, CountryCode countryCode)
    {
        return Location.Create(city, countryCode).Value;
    }

    private WeatherReading CreateWeatherReading(double temperatureC, double humidityPercent, double windSpeedKmh)
    {
        return WeatherReading.Create(
            Temperature.FromCelsius(temperatureC).Value,
            Humidity.FromPercent(humidityPercent).Value,
            WindSpeed.FromKilometersPerHour(windSpeedKmh).Value,
            UtcNow);
    }

    private static AlertThresholds CreateCustomThresholds(
        double highTempC,
        double lowTempC,
        double highWindKmh,
        double highHumidityPercent,
        double lowHumidityPercent)
    {
        return AlertThresholds.Create(
            Temperature.FromCelsius(highTempC).Value,
            Temperature.FromCelsius(lowTempC).Value,
            WindSpeed.FromKilometersPerHour(highWindKmh).Value,
            Humidity.FromPercent(highHumidityPercent).Value,
            Humidity.FromPercent(lowHumidityPercent).Value).Value;
    }
}
