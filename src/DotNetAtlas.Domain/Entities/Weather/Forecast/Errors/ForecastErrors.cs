using DotNetAtlas.Domain.Common.Errors;

namespace DotNetAtlas.Domain.Entities.Weather.Forecast.Errors;

public static class ForecastErrors
{
    public static NotFoundError CityNotFoundError(string city, CountryCode countryCode)
        => new NotFoundError("City", $"{city},{countryCode}", "WeatherForecast.CityNotFound");
}
