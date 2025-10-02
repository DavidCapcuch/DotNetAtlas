using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;

namespace DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;

public interface IWeatherAlertJobScheduler
{
    void ScheduleAlertJobForGroup(AlertSubscriptionDto alertSubscriptionDto, string groupName);
    void RemoveAlertJobForGroup(string groupName);
    void TriggerAlertJobForGroup(AlertSubscriptionDto alertSubscriptionDto, string groupName);
}
