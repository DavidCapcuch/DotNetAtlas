using Ardalis.Specification;
using FluentResults;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Domain.Alerts.Specifications.AlertConditions;

/// <summary>
/// Specification for detecting high humidity alert conditions.
/// </summary>
public sealed class HighHumidityWeatherAlertSpec : WeatherAlertSpec
{
    /// <summary>
    /// Humidity difference from threshold that escalates alert to Critical severity (in percentage points).
    /// </summary>
    private const double CriticalDifferencePercent = 5.0;

    private readonly AlertThresholds _thresholds;

    public override AlertType AlertType => AlertType.HighHumidity;

    public HighHumidityWeatherAlertSpec(AlertThresholds thresholds)
    {
        _thresholds = thresholds;

        // Use internal Percent property for LINQ expression tree (DB queries + IsSatisfiedBy)
        Query.Where(r => r.Humidity.Percent > thresholds.HighHumidity.Percent);
    }

    public override Result<WeatherAlert> CreateAlert(WeatherReading reading)
    {
        var difference = reading.Humidity.Difference(_thresholds.HighHumidity);

        var severity = difference > CriticalDifferencePercent
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        var message = $"High humidity alert: {reading.Humidity.Format()} " +
                      $"(threshold: {_thresholds.HighHumidity.Format()})";

        return WeatherAlert.Create(AlertType.HighHumidity, severity, message);
    }
}
