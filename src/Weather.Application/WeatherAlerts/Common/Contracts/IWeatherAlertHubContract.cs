using TypedSignalR.Client;

namespace Weather.Application.WeatherAlerts.Common.Contracts;

[Hub]
public interface IWeatherAlertHubContract
{
    Task SubscribeForLocationAlerts(AlertSubscriptionDto alertSubscriptionDto);
    Task UnsubscribeFromLocationAlerts(AlertSubscriptionDto alertSubscriptionDto);
}
