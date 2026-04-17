using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Weather.Application.WeatherAlerts.Common.Contracts;
using Weather.Application.WeatherAlerts.RecordWeatherReading;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.Entities;
using Weather.Domain.Alerts.ValueObjects;
using Weather.Domain.Common.ValueObjects;
using Weather.FunctionalTests.Common;
using Weather.FunctionalTests.Common.TestClientInfrastructure;

namespace Weather.FunctionalTests.SignalR;

/// <summary>
/// Functional tests for the SubscribeForLocationAlerts hub method.
/// Tests verify the complete end-to-end flow:
/// Subscribe → RecordWeatherReadingCommand → MonitoredLocation aggregate → WeatherAlertIssuedDomainEvent → Handler → SignalR notification.
/// </summary>
[Collection<FunctionalTestCollection>]
public class SubscribeForLocationAlertsHubTests : BaseApiTest
{
    private const double SafeTemperatureC = 20.0;
    private const double SafeHumidityPercent = 50.0;
    private const double SafeWindSpeedKmh = 15.0;
    private static readonly AlertThresholds DefaultAlertThresholds = AlertThresholds.CreateDefault();

    // Timeout for consuming messages
    private static readonly TimeSpan ExpectMessageTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExpectNoMessageTimeout = TimeSpan.FromMilliseconds(500);

    private readonly ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult> _recordWeatherReadingHandler;

    public SubscribeForLocationAlertsHubTests(ApiTestFixture app)
        : base(app)
    {
        _recordWeatherReadingHandler = Scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult>>();
    }

    [Fact]
    public async Task WhenSubscribed_AndHighTemperatureRecorded_ReceivesExactlyOneHighTemperatureAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Prague", CountryCode.CZ);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Prague", CountryCode.CZ);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        var command = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };
        await _recordWeatherReadingHandler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly 1 high temperature alert
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            messages.Should().ContainSingle("only one threshold was exceeded");
            messages[0].Message.Should().Contain("temperature", "alert should be about temperature");
        }
    }

    [Fact]
    public async Task WhenSubscribedAsNonAuthenticatedUser_AndHighTemperatureRecorded_ReceivesExactlyOneAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Berlin", CountryCode.DE);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.NonAuth);
        var alertSubscriptionDto = new AlertSubscriptionDto("Berlin", CountryCode.DE);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        var command = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };
        await _recordWeatherReadingHandler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            messages.Should().ContainSingle();
            messages[0].Message.Should().Contain("temperature");
        }
    }

    [Fact]
    public async Task WhenSubscribedToMultipleLocations_ReceivesExactlyOneAlertFromEachLocation()
    {
        // Arrange
        var pragueLocation = await SetupPersistedMonitoredLocationAsync("Prague", CountryCode.CZ);
        var berlinLocation = await SetupPersistedMonitoredLocationAsync("Berlin", CountryCode.DE);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var pragueSubscription = new AlertSubscriptionDto("Prague", CountryCode.CZ);
        var berlinSubscription = new AlertSubscriptionDto("Berlin", CountryCode.DE);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(pragueSubscription);
        await signalRClient.SubscribeForLocationAlertsAsync(berlinSubscription);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = pragueLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = berlinLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly 2 alerts (one from each location)
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            messages.Should().HaveCount(2, "should receive exactly one alert from each location");
            messages.Should().AllSatisfy(m => m.Message.Should().Contain("temperature"));
        }
    }

    [Fact]
    public async Task WhenMultipleClientsSubscribedToSameLocation_AllClientsReceiveExactlyOneAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("London", CountryCode.GB);
        await using var client1 = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        await using var client2 = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        await using var client3 = await SignalRClientFactory.CreateAsync(ClientType.NonAuth);
        var alertSubscriptionDto = new AlertSubscriptionDto("London", CountryCode.GB);

        // Act
        await client1.SubscribeForLocationAlertsAsync(alertSubscriptionDto);
        await client2.SubscribeForLocationAlertsAsync(alertSubscriptionDto);
        await client3.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - All clients should receive exactly one alert each (use Task.WhenAll for parallel consumption)
        var ct = TestContext.Current.CancellationToken;
        var client1Task = client1.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5, ct);
        var client2Task = client2.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5, ct);
        var client3Task = client3.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5, ct);
        await Task.WhenAll(client1Task, client2Task, client3Task);
        var client1Messages = await client1Task;
        var client2Messages = await client2Task;
        var client3Messages = await client3Task;

        using (new AssertionScope())
        {
            client1Messages.Should().ContainSingle("client1 should receive exactly one alert");
            client2Messages.Should().ContainSingle("client2 should receive exactly one alert");
            client3Messages.Should().ContainSingle("client3 should receive exactly one alert");
        }
    }

    [Fact]
    public async Task WhenNotSubscribed_DoesNotReceiveAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Vienna", CountryCode.AT);
        await using var subscribedClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        await using var unsubscribedClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Vienna", CountryCode.AT);

        // Act
        await subscribedClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert (use Task.WhenAll for parallel consumption)
        var ct = TestContext.Current.CancellationToken;
        var subscribedTask = subscribedClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5, ct);
        var unsubscribedTask = unsubscribedClient.ConsumeMultiple(ExpectNoMessageTimeout, maxCount: 5, ct);
        await Task.WhenAll(subscribedTask, unsubscribedTask);
        var subscribedMessages = await subscribedTask;
        var unsubscribedMessages = await unsubscribedTask;

        using (new AssertionScope())
        {
            subscribedMessages.Should().ContainSingle("subscribed client should receive exactly one alert");
            unsubscribedMessages.Should().BeEmpty("unsubscribed client should not receive any alert");
        }
    }

    [Fact]
    public async Task WhenSubscribingWithEmptyCity_ThrowsHubException()
    {
        // Arrange
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var invalidSubscription = new AlertSubscriptionDto("", CountryCode.CZ);

        // Act & Assert
        await signalRClient.Invoking(async c =>
                await c.SubscribeForLocationAlertsAsync(invalidSubscription))
            .Should()
            .ThrowAsync<HubException>();
    }

    [Fact]
    public async Task WhenSubscribingWithCityTooShort_ThrowsHubException()
    {
        // Arrange
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var invalidSubscription = new AlertSubscriptionDto("A", CountryCode.CZ); // City must be at least 2 characters

        // Act & Assert
        await signalRClient.Invoking(async c =>
                await c.SubscribeForLocationAlertsAsync(invalidSubscription))
            .Should()
            .ThrowAsync<HubException>();
    }

    [Fact]
    public async Task WhenSubscribingWithCityTooLong_ThrowsHubException()
    {
        // Arrange
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var veryLongCity = new string('X', 101); // City max is 100 characters
        var invalidSubscription = new AlertSubscriptionDto(veryLongCity, CountryCode.CZ);

        // Act & Assert
        await signalRClient.Invoking(async c =>
                await c.SubscribeForLocationAlertsAsync(invalidSubscription))
            .Should()
            .ThrowAsync<HubException>();
    }

    [Fact]
    public async Task WhenSubscribingTwiceToSameLocation_ReceivesExactlyOneAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Madrid", CountryCode.ES);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Madrid", CountryCode.ES);

        // Act - Subscribe twice to same location (should be idempotent)
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly one message (not duplicated)
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        messages.Should().ContainSingle("duplicate subscription should not cause duplicate messages");
    }

    [Fact]
    public async Task WhenSubscribedToDifferentCountryWithSameCity_OnlyReceivesAlertsForSubscribedCountry()
    {
        // Arrange
        var usLocation = await SetupPersistedMonitoredLocationAsync("Portland", CountryCode.US);
        await SetupPersistedMonitoredLocationAsync("Portland", CountryCode.GB);
        await using var usClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        await using var gbClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var usSubscription = new AlertSubscriptionDto("Portland", CountryCode.US);
        var gbSubscription = new AlertSubscriptionDto("Portland", CountryCode.GB);

        // Act
        await usClient.SubscribeForLocationAlertsAsync(usSubscription);
        await gbClient.SubscribeForLocationAlertsAsync(gbSubscription);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = usLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert (use Task.WhenAll for parallel consumption)
        var ct = TestContext.Current.CancellationToken;
        var usTask = usClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5, ct);
        var gbTask = gbClient.ConsumeMultiple(ExpectNoMessageTimeout, maxCount: 5, ct);
        await Task.WhenAll(usTask, gbTask);
        var usMessages = await usTask;
        var gbMessages = await gbTask;

        using (new AssertionScope())
        {
            usMessages.Should().ContainSingle("US client should receive exactly one alert for US Portland");
            gbMessages.Should().BeEmpty("GB client should not receive alert for US Portland");
        }
    }

    [Fact]
    public async Task WhenNormalReadingRecorded_DoesNotReceiveAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Paris", CountryCode.FR);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Paris", CountryCode.FR);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = SafeTemperatureC,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should not receive any alert
        var messages = await signalRClient.ConsumeMultiple(ExpectNoMessageTimeout, maxCount: 5,
            TestContext.Current.CancellationToken);

        messages.Should().BeEmpty("no alert should be sent when no threshold is exceeded");
    }

    [Fact]
    public async Task WhenHighWindRecorded_ReceivesExactlyOneHighWindAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Dublin", CountryCode.IE);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Dublin", CountryCode.IE);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = SafeTemperatureC,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = DefaultAlertThresholds.HighWindSpeed.In(WindSpeedUnit.KilometersPerHour) + 20,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly 1 high wind alert
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            messages.Should().ContainSingle(m => m.Message.Contains("wind"));
            messages[0].Message.Should().Contain("wind", "alert should be about wind");
        }
    }

    [Fact]
    public async Task WhenLowTemperatureRecorded_ReceivesExactlyOneLowTemperatureAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Oslo", CountryCode.NO);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Oslo", CountryCode.NO);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.LowTemperature.In(TemperatureUnit.Celsius) - 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly 1 low temperature alert
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            messages.Should().ContainSingle("only low temperature threshold was exceeded");
            messages[0].Message.Should().Contain("temperature", "alert should be about temperature");
        }
    }

    [Fact]
    public async Task WhenHighHumidityRecorded_ReceivesExactlyOneHighHumidityAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Singapore", CountryCode.SG);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Singapore", CountryCode.SG);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = SafeTemperatureC,
                    HumidityPercent = DefaultAlertThresholds.HighHumidity.Value + 5,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly 1 high humidity alert
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            messages.Should().ContainSingle("only humidity threshold was exceeded");
            messages[0].Message.Should().Contain("humidity", "alert should be about humidity");
        }
    }

    [Fact]
    public async Task WhenLowHumidityRecorded_ReceivesExactlyOneLowHumidityAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Phoenix", CountryCode.US);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Phoenix", CountryCode.US);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = SafeTemperatureC,
                    HumidityPercent = DefaultAlertThresholds.LowHumidity.Value - 5,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly 1 low humidity alert
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            messages.Should().ContainSingle("only low humidity threshold was exceeded");
            messages[0].Message.Should().Contain("humidity", "alert should be about humidity");
        }
    }

    [Fact]
    public async Task WhenMultipleThresholdsExceeded_ReceivesMultipleAlerts()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Extreme", CountryCode.AU);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Extreme", CountryCode.AU);

        // Act
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        // Record reading that exceeds both high temperature AND high wind thresholds
        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultAlertThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 10,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = DefaultAlertThresholds.HighWindSpeed.In(WindSpeedUnit.KilometersPerHour) + 20,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly 2 alerts (one for temperature, one for wind)
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            messages.Should().HaveCount(2, "both temperature and wind thresholds were exceeded");
            messages.Should().Contain(m => m.Message.Contains("temperature"), "should have temperature alert");
            messages.Should().Contain(m => m.Message.Contains("wind"), "should have wind alert");
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
