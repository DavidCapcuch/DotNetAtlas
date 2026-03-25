using Ardalis.Specification;
using FluentResults;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Domain.Alerts.Specifications.AlertConditions;

/// <summary>
/// Specification for detecting low humidity alert conditions.
/// </summary>
public sealed class LowHumidityWeatherAlertSpec : WeatherAlertSpec
{
    /// <summary>
    /// Humidity difference from threshold that escalates alert to Critical severity (in percentage points).
    /// </summary>
    private const double CriticalDifferencePercent = 5.0;

    private readonly AlertThresholds _thresholds;

    public override AlertType AlertType => AlertType.LowHumidity;

    public LowHumidityWeatherAlertSpec(AlertThresholds thresholds)
    {
        _thresholds = thresholds;

        // Use internal Percent property for LINQ expression tree (DB queries + IsSatisfiedBy)
        Query.Where(r => r.Humidity.Percent < thresholds.LowHumidity.Percent);
    }

    public override Result<WeatherAlert> CreateAlert(WeatherReading reading)
    {
        // Difference is negative for low humidity (actual < threshold)
        var difference = _thresholds.LowHumidity.Difference(reading.Humidity);

        var severity = difference > CriticalDifferencePercent
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        var message = $"Low humidity alert: {reading.Humidity.Format()} " +
                      $"(threshold: {_thresholds.LowHumidity.Format()})";

        return WeatherAlert.Create(AlertType.LowHumidity, severity, message);
    }
}
