using DotNetAtlas.SharedKernel.Errors;

namespace DotNetAtlas.Domain.Alerts.Errors;

public static class WindSpeedErrors
{
    public static ValidationError InvalidWindSpeed(double value)
        => new(
            propertyName: "WindSpeed",
            errorMessage: $"Wind speed cannot be negative. Got: {value}",
            errorCode: "WindSpeed.InvalidWindSpeed");
}
