using DotNetAtlas.SharedKernel.Errors;

namespace DotNetAtlas.Domain.Alerts.Errors;

public static class TemperatureErrors
{
    public static ValidationError InvalidTemperature(double value, string unit)
        => new(
            propertyName: "Temperature",
            errorMessage: $"Temperature cannot be below absolute zero. Got: {value} {unit}",
            errorCode: "Temperature.InvalidTemperature");
}
