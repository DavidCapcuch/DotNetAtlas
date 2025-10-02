using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;

namespace DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;

public interface IWeatherAlertNotifier
{
    Task SendWeatherAlert(WeatherAlert weatherAlert);
}
