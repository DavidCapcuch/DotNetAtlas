using TypedSignalR.Client;

namespace DotNetAtlas.Application.WeatherAlerts.Common.Contracts;

[Receiver]
public interface IWeatherAlertClientContract
{
    Task ReceiveWeatherAlert(WeatherAlertMessageDto weatherAlertMessageDto);
}
