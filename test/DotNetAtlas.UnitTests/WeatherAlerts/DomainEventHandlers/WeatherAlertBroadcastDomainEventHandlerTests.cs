using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;
using DotNetAtlas.Domain.Alerts.Events;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Domain.Common.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DotNetAtlas.UnitTests.WeatherAlerts.DomainEventHandlers;

/// <summary>
/// Unit tests for WeatherAlertRealTimeNotificationDomainEventHandler.
/// Uses a fake hub service to capture sent alerts for verification.
/// </summary>
public class WeatherAlertBroadcastDomainEventHandlerTests
{
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.UtcNow;

    private readonly FakeWeatherAlertBroadcaster _fakeBroadcaster;
    private readonly WeatherAlertBroadcastDomainEventHandler _handler;

    public WeatherAlertBroadcastDomainEventHandlerTests()
    {
        _fakeBroadcaster = new FakeWeatherAlertBroadcaster();

        _handler = new WeatherAlertBroadcastDomainEventHandler(
            Substitute.For<ILogger<WeatherAlertBroadcastDomainEventHandler>>(),
            _fakeBroadcaster);
    }

    [Fact]
    public async Task Handle_WhenValidCity_SendsAlertToCorrectGroup()
    {
        // Arrange
        var domainEvent = CreateDomainEvent("Prague", CountryCode.CZ);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            _fakeBroadcaster.SentAlerts.Should().ContainSingle();
            var (alertGroup, weatherAlert) = _fakeBroadcaster.SentAlerts[0];
            alertGroup.GroupName.Should().Be("PRAGUE:CZ");
            weatherAlert.Message.Should().Be(domainEvent.WeatherAlert.Message);
        }
    }

    [Fact]
    public async Task Handle_WhenDifferentCountryCode_SendsToCorrectGroup()
    {
        // Arrange
        var domainEvent = CreateDomainEvent("Madrid", CountryCode.ES);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _fakeBroadcaster.SentAlerts.Should().ContainSingle()
            .Which.AlertGroup.GroupName.Should().Be("MADRID:ES");
    }

    [Fact]
    public async Task Handle_WhenCityWithSpaces_SendsToCorrectGroup()
    {
        // Arrange
        var domainEvent = CreateDomainEvent("New York", CountryCode.US);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _fakeBroadcaster.SentAlerts.Should().ContainSingle()
            .Which.AlertGroup.GroupName.Should().Be("NEW YORK:US");
    }

    [Fact]
    public async Task Handle_WhenLowercaseCity_NormalizesToUppercase()
    {
        // Arrange
        var domainEvent = CreateDomainEvent("london", CountryCode.GB);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _fakeBroadcaster.SentAlerts.Should().ContainSingle()
            .Which.AlertGroup.GroupName.Should().Be("LONDON:GB");
    }

    [Fact]
    public async Task Handle_PassesCorrectMessageToNotifier()
    {
        // Arrange
        const string expectedMessage = "Critical: Temperature reached 45°C!";
        var domainEvent = CreateDomainEvent(message: expectedMessage);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _fakeBroadcaster.SentAlerts.Should().ContainSingle()
            .Which.WeatherAlert.Message.Should().Be(expectedMessage);
    }

    private static WeatherAlertIssuedDomainEvent CreateDomainEvent(
        string city = "Prague",
        CountryCode? countryCode = null,
        string message = "High temperature alert: 40°C")
    {
        return new WeatherAlertIssuedDomainEvent
        {
            MonitoredLocationId = Guid.CreateVersion7(),
            City = City.Create(city).Value,
            CountryCode = countryCode ?? CountryCode.CZ,
            WeatherAlert = WeatherAlert.Create(AlertType.HighTemperature, AlertSeverity.Warning, message).Value,
            TriggeringReading = WeatherReading.Create(
                Temperature.FromCelsius(40).Value,
                Humidity.FromPercent(50).Value,
                WindSpeed.FromKilometersPerHour(15).Value,
                UtcNow),
            IssuedAtUtc = UtcNow
        };
    }

    /// <summary>
    /// Fake implementation of IWeatherAlertBroadcaster that captures sent alerts for verification.
    /// </summary>
    private sealed class FakeWeatherAlertBroadcaster : IWeatherAlertBroadcaster
    {
        public List<(AlertGroup AlertGroup, WeatherAlert WeatherAlert)> SentAlerts { get; } = [];
        public List<(string ConnectionId, AlertGroup AlertGroup)> AddedConnections { get; } = [];
        public List<(string ConnectionId, AlertGroup AlertGroup)> RemovedConnections { get; } = [];

        public Task AddConnectionToGroupAsync(string connectionId, AlertGroup alertGroup, CancellationToken ct)
        {
            AddedConnections.Add((connectionId, alertGroup));
            return Task.CompletedTask;
        }

        public Task RemoveConnectionFromGroupAsync(string connectionId, AlertGroup alertGroup, CancellationToken ct)
        {
            RemovedConnections.Add((connectionId, alertGroup));
            return Task.CompletedTask;
        }

        public Task BroadcastToGroupAsync(AlertGroup alertGroup, WeatherAlert weatherAlert)
        {
            SentAlerts.Add((alertGroup, weatherAlert));
            return Task.CompletedTask;
        }
    }
}
