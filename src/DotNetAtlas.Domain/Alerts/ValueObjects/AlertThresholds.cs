using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.SharedKernel.Base;
using FluentResults;

namespace DotNetAtlas.Domain.Alerts.ValueObjects;

/// <summary>
/// Value object containing configurable alert thresholds for a monitored location.
/// When weather readings exceed these thresholds, alerts are issued.
/// </summary>
/// <remarks>
/// Thresholds are composed of domain value objects (Temperature, WindSpeed, Humidity) for type safety
/// and consistency with WeatherReading comparisons.
/// </remarks>
public sealed record AlertThresholds : ValueObject
{
    /// <summary>
    /// Temperature threshold for high temperature alerts.
    /// Alerts issued when reading exceeds this value.
    /// </summary>
    public Temperature HighTemperature { get; private init; } = null!;

    /// <summary>
    /// Temperature threshold for low temperature alerts.
    /// Alerts issued when reading falls below this value.
    /// </summary>
    public Temperature LowTemperature { get; private init; } = null!;

    /// <summary>
    /// Wind speed threshold for high wind alerts.
    /// Alerts issued when reading exceeds this value.
    /// </summary>
    public WindSpeed HighWindSpeed { get; private init; } = null!;

    /// <summary>
    /// Humidity threshold for high humidity alerts.
    /// Alerts issued when reading exceeds this value.
    /// </summary>
    public Humidity HighHumidity { get; private init; } = null!;

    /// <summary>
    /// Humidity threshold for low humidity alerts.
    /// Alerts issued when reading falls below this value.
    /// </summary>
    public Humidity LowHumidity { get; private init; } = null!;

    private AlertThresholds()
    {
    }

    /// <summary>
    /// Creates alert thresholds with specified value objects.
    /// </summary>
    /// <param name="highTemperatureThreshold">High temperature threshold.</param>
    /// <param name="lowTemperatureThreshold">Low temperature threshold.</param>
    /// <param name="highWindSpeedThreshold">High wind speed threshold.</param>
    /// <param name="highHumidityThreshold">High humidity threshold.</param>
    /// <param name="lowHumidityThreshold">Low humidity threshold.</param>
    /// <returns>Result containing the AlertThresholds or validation errors.</returns>
    public static Result<AlertThresholds> Create(
        Temperature highTemperatureThreshold,
        Temperature lowTemperatureThreshold,
        WindSpeed highWindSpeedThreshold,
        Humidity highHumidityThreshold,
        Humidity lowHumidityThreshold)
    {
        if (lowTemperatureThreshold >= highTemperatureThreshold)
        {
            return Result.Fail(AlertThresholdsErrors.LowTemperatureMustBeLessThanHigh(
                lowTemperatureThreshold.In(TemperatureUnit.Celsius),
                highTemperatureThreshold.In(TemperatureUnit.Celsius)));
        }

        if (lowHumidityThreshold >= highHumidityThreshold)
        {
            return Result.Fail(AlertThresholdsErrors.LowHumidityMustBeLessThanHigh(
                lowHumidityThreshold.Value,
                highHumidityThreshold.Value));
        }

        return new AlertThresholds
        {
            HighTemperature = highTemperatureThreshold,
            LowTemperature = lowTemperatureThreshold,
            HighWindSpeed = highWindSpeedThreshold,
            HighHumidity = highHumidityThreshold,
            LowHumidity = lowHumidityThreshold
        };
    }

    /// <summary>
    /// Creates default alert thresholds with sensible values for general weather monitoring.
    /// </summary>
    /// <remarks>
    /// Default values:
    /// - High Temperature: 35°C (hot day)
    /// - Low Temperature: -10°C (freezing)
    /// - High Wind Speed: 80 km/h (storm warning)
    /// - High Humidity: 90% (very humid)
    /// - Low Humidity: 20% (very dry).
    /// </remarks>
    public static AlertThresholds CreateDefault()
    {
        // These are known safe values - we can use .Value directly
        return new AlertThresholds
        {
            HighTemperature = Temperature.FromCelsius(35.0).Value,
            LowTemperature = Temperature.FromCelsius(-10.0).Value,
            HighWindSpeed = WindSpeed.FromKilometersPerHour(80.0).Value,
            HighHumidity = Humidity.FromPercent(90.0).Value,
            LowHumidity = Humidity.FromPercent(20.0).Value
        };
    }
}
