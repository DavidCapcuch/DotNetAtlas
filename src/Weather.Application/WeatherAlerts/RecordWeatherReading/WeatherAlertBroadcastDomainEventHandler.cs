using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;
using Weather.Application.WeatherAlerts.Common.Abstractions;
using Weather.Domain.Alerts.Events;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Application.WeatherAlerts.RecordWeatherReading;

/// <summary>
/// Handles <see cref="WeatherAlertIssuedDomainEvent"/> by sending real-time SignalR notifications
/// to connected clients subscribed to the location's alert group.
/// </summary>
/// <remarks>
/// This handler is responsible only for real-time push notifications via SignalR.
/// Email notifications are handled separately by <see cref="WeatherAlertEmailNotificationDomainEventHandler"/>.
/// </remarks>
public sealed class
    WeatherAlertBroadcastDomainEventHandler : IDomainEventHandler<WeatherAlertIssuedDomainEvent>
{
    private readonly ILogger<WeatherAlertBroadcastDomainEventHandler> _logger;
    private readonly IWeatherAlertBroadcaster _weatherAlertBroadcaster;

    public WeatherAlertBroadcastDomainEventHandler(
        ILogger<WeatherAlertBroadcastDomainEventHandler> logger,
        IWeatherAlertBroadcaster weatherAlertBroadcaster)
    {
        _logger = logger;
        _weatherAlertBroadcaster = weatherAlertBroadcaster;
    }

    public async Task Handle(WeatherAlertIssuedDomainEvent domainEvent, CancellationToken ct)
    {
        var alertGroup = AlertGroup.From(domainEvent.City, domainEvent.CountryCode);
        await _weatherAlertBroadcaster.BroadcastToGroupAsync(alertGroup, domainEvent.WeatherAlert);

        _logger.LogInformation(
            "Sent real-time weather alert for {City}:{CountryCode}. Type: {AlertType}, Severity: {Severity}",
            domainEvent.City.Name, domainEvent.CountryCode, domainEvent.WeatherAlert.Type,
            domainEvent.WeatherAlert.Severity);
    }
}
