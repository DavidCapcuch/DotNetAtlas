using Ardalis.Specification;
using FluentResults;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Domain.Alerts.Specifications.AlertConditions;

/// <summary>
/// Specification for detecting high temperature alert conditions.
/// </summary>
public sealed class HighTemperatureWeatherAlertSpec : WeatherAlertSpec
{
    /// <summary>
    /// Temperature difference from threshold that escalates alert to Critical severity (in Celsius).
    /// </summary>
    private const double CriticalDifferenceCelsius = 5.0;

    private readonly AlertThresholds _thresholds;

    public override AlertType AlertType => AlertType.HighTemperature;

    public HighTemperatureWeatherAlertSpec(AlertThresholds thresholds)
    {
        _thresholds = thresholds;

        // Use internal Celsius property for LINQ expression tree (DB queries + IsSatisfiedBy)
        Query.Where(r => r.Temperature.Celsius > thresholds.HighTemperature.Celsius);
    }

    public override Result<WeatherAlert> CreateAlert(WeatherReading reading)
    {
        var difference = reading.Temperature.DifferenceIn(_thresholds.HighTemperature, TemperatureUnit.Celsius);

        var severity = difference > CriticalDifferenceCelsius
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        var message = $"High temperature alert: {reading.Temperature.Format(TemperatureUnit.Celsius)} " +
                      $"(threshold: {_thresholds.HighTemperature.Format(TemperatureUnit.Celsius)})";

        return WeatherAlert.Create(AlertType.HighTemperature, severity, message);
    }
}
