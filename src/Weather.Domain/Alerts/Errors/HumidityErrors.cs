using Platform.SharedKernel.Errors;

namespace Weather.Domain.Alerts.Errors;

public static class HumidityErrors
{
    public static ValidationError InvalidHumidity(double value)
        => new(
            propertyName: "HumidityPercent",
            errorMessage: $"Humidity must be between 0 and 100 percent. Got: {value}",
            errorCode: "Humidity.InvalidHumidity");
}
