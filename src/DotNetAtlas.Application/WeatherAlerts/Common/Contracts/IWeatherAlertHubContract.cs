using TypedSignalR.Client;

namespace DotNetAtlas.Application.WeatherAlerts.Common.Contracts;

[Hub]
public interface IWeatherAlertHubContract
{
    Task SubscribeForCityAlerts(AlertSubscriptionDto alertSubscriptionDto);
    Task UnsubscribeFromCityAlerts(AlertSubscriptionDto alertSubscriptionDto);
    Task SendWeatherAlert(IAsyncEnumerable<WeatherAlert> weatherAlerts);
}
