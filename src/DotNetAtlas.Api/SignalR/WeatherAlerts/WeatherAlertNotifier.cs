using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherAlerts.Common;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace DotNetAtlas.Api.SignalR.WeatherAlerts;

public class WeatherAlertNotifier : IWeatherAlertNotifier
{
    private readonly IHubContext<WeatherAlertHub, IWeatherAlertClientContract> _hubContext;
    private readonly ILogger<WeatherAlertNotifier> _logger;
    private readonly IDotNetAtlasInstrumentation _dotNetAtlasInstrumentation;

    public WeatherAlertNotifier(
        IHubContext<WeatherAlertHub, IWeatherAlertClientContract> hubContext,
        ILogger<WeatherAlertNotifier> logger,
        IDotNetAtlasInstrumentation dotNetAtlasInstrumentation)
    {
        _hubContext = hubContext;
        _logger = logger;
        _dotNetAtlasInstrumentation = dotNetAtlasInstrumentation;
    }

    public async Task SendWeatherAlert(WeatherAlert weatherAlert)
    {
        using var activity = _dotNetAtlasInstrumentation.StartActivity(nameof(SendWeatherAlert));

        var group = WeatherAlertGroupNames.GroupByCitySubscriptionRequest(
            new AlertSubscriptionDto(weatherAlert.City, weatherAlert.CountryCode));

        activity?.SetTag(DiagnosticNames.City, weatherAlert.City);
        activity?.SetTag(DiagnosticNames.CountryCode, weatherAlert.CountryCode.ToString());
        activity?.SetTag(DiagnosticNames.SignalRGroup, group);
        activity?.SetTag(DiagnosticNames.SignalRPayloadSize, weatherAlert.AlertMessage);

        _logger.LogInformation(
            "Notifying group {Group} with Weather Alert {Message}",
            group, weatherAlert.AlertMessage);

        await _hubContext.Clients
            .Group(group)
            .ReceiveWeatherAlert(new WeatherAlertMessage(weatherAlert.AlertMessage));
    }
}
