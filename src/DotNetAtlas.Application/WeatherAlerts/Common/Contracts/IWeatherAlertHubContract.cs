using TypedSignalR.Client;

namespace DotNetAtlas.Application.WeatherAlerts.Common.Contracts;

[Hub]
public interface IWeatherAlertHubContract
{
    Task SubscribeForLocationAlerts(AlertSubscriptionDto alertSubscriptionDto);
    Task UnsubscribeFromLocationAlerts(AlertSubscriptionDto alertSubscriptionDto);
}
