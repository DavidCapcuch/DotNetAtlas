using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts;
using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.FunctionalTests.Common;
using DotNetAtlas.FunctionalTests.Common.Clients;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.FunctionalTests.SignalR;

/// <summary>
/// Functional tests for the UnsubscribeFromLocationAlerts hub method.
/// Tests verify the complete end-to-end flow:
/// Subscribe → Unsubscribe → RecordWeatherReadingCommand → No SignalR notification received.
/// </summary>
[Collection<SignalRTestCollection>]
public class UnsubscribeFromLocationAlertsHubTests : BaseApiTest
{
    // Values used in tests that should NOT trigger alerts (within safe ranges)
    private const double SafeHumidityPercent = 50.0;
    private const double SafeWindSpeedKmh = 15.0;

    private static readonly AlertThresholds DefaultThresholds = AlertThresholds.CreateDefault();

    private static readonly TimeSpan ExpectMessageTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExpectNoMessageTimeout = TimeSpan.FromMilliseconds(500);

    private readonly ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult> _recordWeatherReadingHandler;

    public UnsubscribeFromLocationAlertsHubTests(ApiTestFixture app)
        : base(app)
    {
        _recordWeatherReadingHandler = Scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult>>();
    }

    [Fact]
    public async Task WhenUnsubscribed_NoLongerReceivesAlerts()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Prague", CountryCode.CZ);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Prague", CountryCode.CZ);

        // Subscribe and verify receiving alerts
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        var firstMessages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);
        firstMessages.Should().ContainSingle("should receive exactly one alert while subscribed");

        // Act - Unsubscribe
        await signalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);

        // Send another alert after unsubscribing
        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 6,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should NOT receive any alert after unsubscribing
        var secondMessages = await signalRClient.ConsumeMultiple(ExpectNoMessageTimeout, maxCount: 5,
            TestContext.Current.CancellationToken);
        secondMessages.Should().BeEmpty("should not receive any alert after unsubscribing");
    }

    [Fact]
    public async Task WhenUnsubscribedFromOneLocation_ReceivesExactlyOneAlertFromRemainingLocation()
    {
        // Arrange
        var pragueLocation = await SetupPersistedMonitoredLocationAsync("Prague", CountryCode.CZ);
        var berlinLocation = await SetupPersistedMonitoredLocationAsync("Berlin", CountryCode.DE);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var pragueSubscription = new AlertSubscriptionDto("Prague", CountryCode.CZ);
        var berlinSubscription = new AlertSubscriptionDto("Berlin", CountryCode.DE);

        // Subscribe to both locations
        await signalRClient.SubscribeForLocationAlertsAsync(pragueSubscription);
        await signalRClient.SubscribeForLocationAlertsAsync(berlinSubscription);

        // Act - Unsubscribe from Prague only
        await signalRClient.UnsubscribeFromCityAlertsAsync(pragueSubscription);

        // Send alerts to both locations
        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = pragueLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
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
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should only receive exactly one alert (from Berlin)
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        messages.Should().ContainSingle("should only receive alert from Berlin, not Prague");
    }

    [Fact]
    public async Task WhenOneClientUnsubscribes_OtherClientReceivesExactlyOneAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("London", CountryCode.GB);
        await using var client1 = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        await using var client2 = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("London", CountryCode.GB);

        // Both clients subscribe
        await client1.SubscribeForLocationAlertsAsync(alertSubscriptionDto);
        await client2.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        // Act - Client 1 unsubscribes
        await client1.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);

        // Send alert
        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert (use Task.WhenAll for parallel consumption)
        var ct = TestContext.Current.CancellationToken;
        var client1Task = client1.ConsumeMultiple(ExpectNoMessageTimeout, maxCount: 5, ct);
        var client2Task = client2.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5, ct);
        await Task.WhenAll(client1Task, client2Task);
        var client1Messages = await client1Task;
        var client2Messages = await client2Task;

        using (new AssertionScope())
        {
            client1Messages.Should().BeEmpty("client1 should not receive any alert after unsubscribing");
            client2Messages.Should().ContainSingle("client2 should receive exactly one alert");
        }
    }

    [Fact]
    public async Task WhenUnsubscribingAsNonAuthenticatedUser_NoLongerReceivesAlerts()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Berlin", CountryCode.DE);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.NonAuth);
        var alertSubscriptionDto = new AlertSubscriptionDto("Berlin", CountryCode.DE);

        // Subscribe
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        // Act - Unsubscribe
        await signalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);

        // Send alert
        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert
        var messages = await signalRClient.ConsumeMultiple(ExpectNoMessageTimeout, maxCount: 5,
            TestContext.Current.CancellationToken);
        messages.Should().BeEmpty("should not receive any alert after unsubscribing");
    }

    [Fact]
    public async Task WhenUnsubscribingFromAllLocations_ReceivesNoAlerts()
    {
        // Arrange
        var pragueLocation = await SetupPersistedMonitoredLocationAsync("Prague", CountryCode.CZ);
        var berlinLocation = await SetupPersistedMonitoredLocationAsync("Berlin", CountryCode.DE);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var pragueSubscription = new AlertSubscriptionDto("Prague", CountryCode.CZ);
        var berlinSubscription = new AlertSubscriptionDto("Berlin", CountryCode.DE);

        // Subscribe to both
        await signalRClient.SubscribeForLocationAlertsAsync(pragueSubscription);
        await signalRClient.SubscribeForLocationAlertsAsync(berlinSubscription);

        // Act - Unsubscribe from both
        await signalRClient.UnsubscribeFromCityAlertsAsync(pragueSubscription);
        await signalRClient.UnsubscribeFromCityAlertsAsync(berlinSubscription);

        // Send alerts to both locations
        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = pragueLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
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
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive no alerts
        var messages = await signalRClient.ConsumeMultiple(ExpectNoMessageTimeout, maxCount: 5,
            TestContext.Current.CancellationToken);
        messages.Should().BeEmpty("should not receive any alerts after unsubscribing from all locations");
    }

    [Fact]
    public async Task WhenUnsubscribingWithEmptyCity_ThrowsHubException()
    {
        // Arrange
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var invalidSubscription = new AlertSubscriptionDto("", CountryCode.CZ);

        // Act & Assert
        await signalRClient.Invoking(async c =>
                await c.UnsubscribeFromCityAlertsAsync(invalidSubscription))
            .Should()
            .ThrowAsync<HubException>();
    }

    [Fact]
    public async Task WhenUnsubscribingWithCityTooShort_ThrowsHubException()
    {
        // Arrange
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var invalidSubscription = new AlertSubscriptionDto("A", CountryCode.CZ);

        // Act & Assert
        await signalRClient.Invoking(async c =>
                await c.UnsubscribeFromCityAlertsAsync(invalidSubscription))
            .Should()
            .ThrowAsync<HubException>();
    }

    [Fact]
    public async Task WhenUnsubscribingWithCityTooLong_ThrowsHubException()
    {
        // Arrange
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var veryLongCity = new string('X', 101);
        var invalidSubscription = new AlertSubscriptionDto(veryLongCity, CountryCode.CZ);

        // Act & Assert
        await signalRClient.Invoking(async c =>
                await c.UnsubscribeFromCityAlertsAsync(invalidSubscription))
            .Should()
            .ThrowAsync<HubException>();
    }

    [Fact]
    public async Task WhenUnsubscribingWithoutPriorSubscription_DoesNotReceiveAlerts()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Vienna", CountryCode.AT);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Vienna", CountryCode.AT);

        // Act - Unsubscribe without subscribing first (idempotent operation)
        await signalRClient.Invoking(async c =>
                await c.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto))
            .Should()
            .ThrowAsync<HubException>();
    }

    [Fact]
    public async Task WhenUnsubscribingTwice_StillDoesNotReceiveAlerts()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Madrid", CountryCode.ES);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Madrid", CountryCode.ES);

        // Subscribe
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        // Act - Unsubscribe twice (idempotent)
        await signalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);
        await signalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);

        // Send alert
        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert
        var messages = await signalRClient.ConsumeMultiple(ExpectNoMessageTimeout, maxCount: 5,
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            signalRClient.Connection.State.Should().Be(HubConnectionState.Connected);
            messages.Should().BeEmpty("should not receive any alert after unsubscribing");
        }
    }

    [Fact]
    public async Task WhenResubscribingAfterUnsubscribe_ReceivesExactlyOneAlert()
    {
        // Arrange
        var monitoredLocation = await SetupPersistedMonitoredLocationAsync("Paris", CountryCode.FR);
        await using var signalRClient = await SignalRClientFactory.CreateAsync(ClientType.RegularUser);
        var alertSubscriptionDto = new AlertSubscriptionDto("Paris", CountryCode.FR);

        // Subscribe, unsubscribe, then resubscribe
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);
        await signalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);
        await signalRClient.SubscribeForLocationAlertsAsync(alertSubscriptionDto);

        // Act - Send alert
        await _recordWeatherReadingHandler.HandleAsync(new RecordWeatherReadingCommand
        {
            MonitoredLocationId = monitoredLocation.Id,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = DefaultThresholds.HighTemperature.In(TemperatureUnit.Celsius) + 5,
                    HumidityPercent = SafeHumidityPercent,
                    WindSpeedKmh = SafeWindSpeedKmh,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        }, TestContext.Current.CancellationToken);

        // Assert - Should receive exactly one alert after resubscribing
        var messages =
            await signalRClient.ConsumeMultiple(ExpectMessageTimeout, maxCount: 5,
                TestContext.Current.CancellationToken);

        messages.Should().ContainSingle("should receive exactly one alert after resubscribing");
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
