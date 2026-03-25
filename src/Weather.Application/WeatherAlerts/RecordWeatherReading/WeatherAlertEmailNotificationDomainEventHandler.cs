using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Weather.Application.Common.Data;
using Weather.Application.Common.Messaging;
using Weather.Domain.Alerts.Events;
using Weather.Domain.Alerts.Specifications;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Application.WeatherAlerts.RecordWeatherReading;

/// <summary>
/// Handles <see cref="WeatherAlertIssuedDomainEvent"/> by adding email notification commands
/// to the transactional outbox for each subscriber of the monitored location.
/// </summary>
/// <remarks>
/// This handler is responsible only for queuing email notifications via the outbox pattern.
/// Real-time SignalR notifications are handled separately by <see cref="WeatherAlertBroadcastDomainEventHandler"/>.
/// </remarks>
public sealed class WeatherAlertEmailNotificationDomainEventHandler : IDomainEventHandler<WeatherAlertIssuedDomainEvent>
{
    private const string TemplateId = "weather-alerts.weather-alert";

    private readonly ILogger<WeatherAlertEmailNotificationDomainEventHandler> _logger;
    private readonly IWeatherDbContext _weatherDbContext;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TopicsOptions _topicsOptions;

    public WeatherAlertEmailNotificationDomainEventHandler(
        ILogger<WeatherAlertEmailNotificationDomainEventHandler> logger,
        IWeatherDbContext weatherDbContext,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutbox,
        IOptions<TopicsOptions> topicsOptions)
    {
        _logger = logger;
        _weatherDbContext = weatherDbContext;
        _transactionalOutbox = transactionalOutbox;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(WeatherAlertIssuedDomainEvent domainEvent, CancellationToken ct)
    {
        var subscriberUserIds = await _weatherDbContext.AlertSubscribers
            .AsNoTracking()
            .WithSpecification(new AlertSubscribersByMonitoredLocationIdSpec(domainEvent.MonitoredLocationId))
            .Select(s => s.UserId)
            .ToArrayAsync(ct);

        // This can potentially be extracted into a separate service
        foreach (var userId in subscriberUserIds)
        {
            var sendAlertEmailNotificationCommand = new SendEmailNotificationCommand
            {
                UserId = userId,
                TemplateId = TemplateId,
                TemplateData = new Dictionary<string, string>
                {
                    ["city"] = domainEvent.City.Name,
                    ["countryCode"] = domainEvent.CountryCode.ToString(),
                    ["alertType"] = domainEvent.WeatherAlert.Type.ToString(),
                    ["severity"] = domainEvent.WeatherAlert.Severity.ToString(),
                    ["message"] = domainEvent.WeatherAlert.Message,
                    ["temperature"] =
                        domainEvent.TriggeringReading.Temperature.Format(TemperatureUnit.Celsius, decimals: 1),
                    ["humidity"] = domainEvent.TriggeringReading.Humidity.Format(decimals: 0),
                    ["windSpeed"] =
                        domainEvent.TriggeringReading.WindSpeed.Format(WindSpeedUnit.KilometersPerHour, decimals: 0)
                },
                IdempotencyKey =
                    $"weather-alert-{userId}-{domainEvent.MonitoredLocationId}-{domainEvent.IssuedAtUtc:O}",
                OccurredOnUtc = domainEvent.IssuedAtUtc.UtcDateTime
            };

            _transactionalOutbox.AddOutboxMessage(
                _topicsOptions.NotificationCommands,
                userId.ToString(),
                sendAlertEmailNotificationCommand);
        }

        await _transactionalOutbox.SaveChangesAsync(ct);

        if (subscriberUserIds.Length > 0)
        {
            _logger.LogDebug(
                "Added {Count} SendEmailNotificationCommand(s) to outbox for weather alert. " +
                "MonitoredLocationId: {MonitoredLocationId}, City: {City}, AlertType: {AlertType}",
                subscriberUserIds.Length,
                domainEvent.MonitoredLocationId,
                domainEvent.City.Name,
                domainEvent.WeatherAlert.Type);
        }
    }
}
