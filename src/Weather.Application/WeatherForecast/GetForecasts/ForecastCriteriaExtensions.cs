using Weather.Domain.Forecast.ValueObjects;

namespace Weather.Application.WeatherForecast.GetForecasts;

public static class ForecastCriteriaExtensions
{
    extension(ForecastCriteria criteria)
    {
        public string CacheKey() =>
            $"forecast:{criteria.City.Name.ToUpperInvariant()}:{criteria.CountryCode}:{criteria.Days}";
    }
}
