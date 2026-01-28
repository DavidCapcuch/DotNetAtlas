using Ardalis.Specification;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using FluentResults;

namespace DotNetAtlas.Domain.Alerts.Specifications.AlertConditions;

/// <summary>
/// Specification for detecting low temperature alert conditions.
/// </summary>
public sealed class LowTemperatureWeatherAlertSpec : WeatherAlertSpec
{
    /// <summary>
    /// Temperature difference from threshold that escalates alert to Critical severity (in Celsius).
    /// </summary>
    private const double CriticalDifferenceCelsius = 5.0;

    private readonly AlertThresholds _thresholds;

    public override AlertType AlertType => AlertType.LowTemperature;

    public LowTemperatureWeatherAlertSpec(AlertThresholds thresholds)
    {
        _thresholds = thresholds;

        // Use internal Celsius property for LINQ expression tree (DB queries + IsSatisfiedBy)
        Query.Where(r => r.Temperature.Celsius < thresholds.LowTemperature.Celsius);
    }

    public override Result<WeatherAlert> CreateAlert(WeatherReading reading)
    {
        // Difference is negative for low temperature (actual < threshold)
        var difference = _thresholds.LowTemperature.DifferenceIn(reading.Temperature, TemperatureUnit.Celsius);

        var severity = difference > CriticalDifferenceCelsius
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        var message = $"Low temperature alert: {reading.Temperature.Format(TemperatureUnit.Celsius)} " +
                      $"(threshold: {_thresholds.LowTemperature.Format(TemperatureUnit.Celsius)})";

        return WeatherAlert.Create(AlertType.LowTemperature, severity, message);
    }
}
