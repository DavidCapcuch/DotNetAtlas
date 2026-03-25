using Platform.SharedKernel.Errors;

namespace Weather.Domain.Alerts.Errors;

public static class AlertThresholdsErrors
{
    public static ValidationError LowTemperatureMustBeLessThanHigh(double lowTemperatureC, double highTemperatureC)
        => new(
            propertyName: "AlertThresholds",
            errorMessage:
            $"Low temperature threshold ({lowTemperatureC}°C) must be less than high temperature threshold ({highTemperatureC}°C).",
            errorCode: "AlertThresholds.LowTemperatureMustBeLessThanHigh");

    public static ValidationError LowHumidityMustBeLessThanHigh(double lowHumidityPercent, double highHumidityPercent)
        => new(
            propertyName: "AlertThresholds",
            errorMessage:
            $"Low humidity threshold ({lowHumidityPercent}%) must be less than high humidity threshold ({highHumidityPercent}%).",
            errorCode: "AlertThresholds.LowHumidityMustBeLessThanHigh");
}
