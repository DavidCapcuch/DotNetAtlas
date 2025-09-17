using DotNetAtlas.Domain.Errors.Base;

namespace DotNetAtlas.Domain.Errors;

public static class WeatherForecastErrors
{
    public static NotFoundError CityNotFoundError(string city, string countryCode)
        => new NotFoundError("City", $"{city},{countryCode}", "WeatherForecast.CityNotFound");
}
