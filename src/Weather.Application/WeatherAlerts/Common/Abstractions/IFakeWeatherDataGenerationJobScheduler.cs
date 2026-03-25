namespace Weather.Application.WeatherAlerts.Common.Abstractions;

public interface IFakeWeatherDataGenerationJobScheduler
{
    void EnsureWeatherGenerationJobSchedule(Guid monitoredLocationId);
    void TriggerFakeWeatherDataGenerationJob(Guid monitoredLocationId);
}
