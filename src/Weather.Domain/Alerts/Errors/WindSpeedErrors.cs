using Platform.SharedKernel.Errors;

namespace Weather.Domain.Alerts.Errors;

public static class WindSpeedErrors
{
    public static ValidationError InvalidWindSpeed(double value)
        => new(
            propertyName: "WindSpeed",
            errorMessage: $"Wind speed cannot be negative. Got: {value}",
            errorCode: "WindSpeed.InvalidWindSpeed");
}
