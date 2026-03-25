using Microsoft.AspNetCore.SignalR;
using Weather.Application.Common.Observability.Tracing;
using Weather.Application.WeatherAlerts.Common.Abstractions;
using Weather.Application.WeatherAlerts.Common.Contracts;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Api.SignalRHubs.WeatherAlerts;

/// <summary>
/// Implementation of <see cref="IWeatherAlertBroadcaster"/> that bridges the application layer
/// with SignalR for real-time weather alert notifications and group management.
/// </summary>
public class WeatherAlertBroadcaster : IWeatherAlertBroadcaster
{
    private readonly IHubContext<WeatherAlertHub, IWeatherAlertClientContract> _hubContext;
    private readonly ILogger<WeatherAlertBroadcaster> _logger;

    public WeatherAlertBroadcaster(
        IHubContext<WeatherAlertHub, IWeatherAlertClientContract> hubContext,
        ILogger<WeatherAlertBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task AddConnectionToGroupAsync(string connectionId, AlertGroup alertGroup, CancellationToken ct)
    {
        await _hubContext.Groups.AddToGroupAsync(connectionId, alertGroup.GroupName, ct);

        _logger.LogDebug(
            "Added connection {ConnectionId} to alert group {GroupName}",
            connectionId, alertGroup.GroupName);
    }

    public async Task RemoveConnectionFromGroupAsync(string connectionId, AlertGroup alertGroup, CancellationToken ct)
    {
        await _hubContext.Groups.RemoveFromGroupAsync(connectionId, alertGroup.GroupName, ct);

        _logger.LogDebug(
            "Removed connection {ConnectionId} from alert group {GroupName}",
            connectionId, alertGroup.GroupName);
    }

    public async Task BroadcastToGroupAsync(AlertGroup alertGroup, WeatherAlert weatherAlert)
    {
        using var activity = DotNetAtlasActivitySource.StartActivity(nameof(BroadcastToGroupAsync));

        activity?.SetTag(TraceTags.City, alertGroup.City.Name);
        activity?.SetTag(TraceTags.CountryCode, alertGroup.CountryCode.ToString());
        activity?.SetTag(TraceTags.SignalRGroup, alertGroup.GroupName);
        activity?.SetTag(TraceTags.SignalRPayloadLength, weatherAlert.Message.Length);

        _logger.LogInformation(
            "Notifying group {Group} with Weather Alert {Message}",
            alertGroup.GroupName, weatherAlert.Message);

        await _hubContext.Clients
            .Group(alertGroup.GroupName)
            .ReceiveWeatherAlert(new WeatherAlertMessageDto(weatherAlert.Message));
    }
}
