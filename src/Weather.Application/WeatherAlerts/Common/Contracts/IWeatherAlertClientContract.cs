using TypedSignalR.Client;

namespace Weather.Application.WeatherAlerts.Common.Contracts;

[Receiver]
public interface IWeatherAlertClientContract
{
    Task ReceiveWeatherAlert(WeatherAlertMessageDto weatherAlertMessageDto);
}
