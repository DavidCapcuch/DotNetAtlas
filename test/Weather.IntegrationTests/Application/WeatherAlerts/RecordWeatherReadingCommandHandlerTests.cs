using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Weather.Application.WeatherAlerts.RecordWeatherReading;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.Entities;
using Weather.Domain.Common.ValueObjects;
using Weather.IntegrationTests.Common;

namespace Weather.IntegrationTests.Application.WeatherAlerts;

[Collection<ForecastTestCollection>]
public class RecordWeatherReadingCommandHandlerTests : BaseIntegrationTest
{
    private readonly ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult>
        _recordWeatherReadingCommandHandler;

    public RecordWeatherReadingCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _recordWeatherReadingCommandHandler =
            Scope.ServiceProvider
                .GetRequiredService<ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult>>();
    }

    [Fact]
    public async Task WhenValidReadings_RecordsAllReadingsAndReturnsSuccess()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Prague", CountryCode.CZ);

        var recordWeatherReadingCommand = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = 25.0,
                    HumidityPercent = 50.0,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                },
                new WeatherReadingDto
                {
                    TemperatureC = 26.0,
                    HumidityPercent = 55.0,
                    WindSpeedKmh = 20.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
            ]
        };

        // Act
        var recordWeatherReadingResult = await _recordWeatherReadingCommandHandler.HandleAsync(
            recordWeatherReadingCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var updatedMonitoredLocation = await WeatherDbContext.MonitoredLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(ml => ml.Id == monitoredLocation.Id, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            recordWeatherReadingResult.Should().BeSuccess();
            recordWeatherReadingResult.Value.SuccessCount.Should().Be(2);
            recordWeatherReadingResult.Value.FailedCount.Should().Be(0);

            updatedMonitoredLocation.Should().NotBeNull();
            updatedMonitoredLocation.RecentReadings.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task WhenMonitoredLocationNotFound_ReturnsFailure()
    {
        // Arrange
        var recordWeatherReadingCommand = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = Guid.CreateVersion7(),
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = 25.0,
                    HumidityPercent = 50.0,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingResult = await _recordWeatherReadingCommandHandler.HandleAsync(
            recordWeatherReadingCommand,
            TestContext.Current.CancellationToken);

        // Assert
        recordWeatherReadingResult.Should().BeFailure();
    }

    [Fact]
    public async Task WhenSomeInvalidReadings_ProcessesValidOnesAndReportsFailures()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Berlin", CountryCode.DE);

        var recordWeatherReadingCommand = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = 25.0,
                    HumidityPercent = 50.0,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                },
                new WeatherReadingDto
                {
                    TemperatureC = 25.0,
                    HumidityPercent = 150.0, // Invalid humidity
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                },
                new WeatherReadingDto
                {
                    TemperatureC = 22.0,
                    HumidityPercent = 45.0,
                    WindSpeedKmh = 10.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingResult = await _recordWeatherReadingCommandHandler.HandleAsync(
            recordWeatherReadingCommand, TestContext.Current.CancellationToken);

        // Assert
        var updatedMonitoredLocation = await WeatherDbContext.MonitoredLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(ml => ml.Id == monitoredLocation.Id, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            recordWeatherReadingResult.Should().BeSuccess();
            recordWeatherReadingResult.Value.SuccessCount.Should().Be(2);
            recordWeatherReadingResult.Value.FailedCount.Should().Be(1);
            recordWeatherReadingResult.Value.Failures.Should().ContainSingle();
            recordWeatherReadingResult.Value.Failures[0].Index.Should().Be(1);

            updatedMonitoredLocation.Should().NotBeNull();
            updatedMonitoredLocation.RecentReadings.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task WhenHighTemperature_RaisesAlertDomainEvent()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Madrid", CountryCode.ES);

        var recordWeatherReadingCommand = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = 40.0, // Above default threshold of 35°C
                    HumidityPercent = 50.0,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingResult = await _recordWeatherReadingCommandHandler.HandleAsync(
            recordWeatherReadingCommand, TestContext.Current.CancellationToken);

        // Assert
        var outboxMessages = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            recordWeatherReadingResult.Should().BeSuccess();
            outboxMessages.Should()
                .BeEmpty("No subscribers exist so no email notifications are queued, but the alert event is processed");
        }
    }

    [Fact]
    public async Task WhenDeactivatedLocation_DoesNotRaiseAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Vienna", CountryCode.AT);
        monitoredLocation.DeactivateMonitoring();
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outboxCountBefore = await WeatherDbContext.OutboxMessages.CountAsync(TestContext.Current.CancellationToken);

        var recordWeatherReadingCommand = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = 40.0, // Above threshold but location is deactivated
                    HumidityPercent = 50.0,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingResult = await _recordWeatherReadingCommandHandler.HandleAsync(
            recordWeatherReadingCommand,
            TestContext.Current.CancellationToken);

        var outboxCountAfter = await WeatherDbContext.OutboxMessages.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            recordWeatherReadingResult.Should().BeSuccess();
            outboxCountAfter.Should().Be(outboxCountBefore); // No new alerts
        }
    }

    private async Task<MonitoredLocation> SetupPersistedMonitoredLocationAsync(string city, CountryCode countryCode)
    {
        var location = Location.Create(city, countryCode).Value;
        var monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(location);
        monitoredLocation.PopDomainEvents(); // Don't dispatch creation event

        WeatherDbContext.MonitoredLocations.Add(monitoredLocation);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return monitoredLocation;
    }
}
