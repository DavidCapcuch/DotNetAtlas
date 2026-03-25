using Platform.SharedKernel.Errors;
using Weather.Domain.Common.ValueObjects;

namespace Weather.Domain.Forecast.Errors;

public static class ForecastErrors
{
    public static NotFoundError CityNotFoundError(string city, CountryCode countryCode)
        => new NotFoundError("City", $"{city},{countryCode}", "Forecast.CityNotFound");

    public static ValidationError InvalidDaysRange(int minDays, int maxDays, int actualDays)
        => new ValidationError(
            propertyName: "Days",
            errorMessage: $"Forecast days must be between {minDays} and {maxDays}. Provided: {actualDays}",
            errorCode: "Forecast.InvalidDaysRange");

    public static ValidationError InvalidTemperatureRange(double minTemperature, double maxTemperature)
        => new ValidationError(
            propertyName: "Temperature",
            errorMessage:
            $"Minimum temperature ({minTemperature}) cannot exceed maximum temperature ({maxTemperature}).",
            errorCode: "Forecast.InvalidTemperatureRange");
}
