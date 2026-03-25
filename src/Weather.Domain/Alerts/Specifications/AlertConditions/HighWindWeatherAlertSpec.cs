using Ardalis.Specification;
using FluentResults;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Domain.Alerts.Specifications.AlertConditions;

/// <summary>
/// Specification for detecting high wind speed alert conditions.
/// </summary>
public sealed class HighWindWeatherAlertSpec : WeatherAlertSpec
{
    /// <summary>
    /// Wind speed difference from threshold that escalates alert to Critical severity (in km/h).
    /// </summary>
    private const double CriticalDifferenceKmh = 20.0;

    private readonly AlertThresholds _thresholds;

    public override AlertType AlertType => AlertType.HighWind;

    public HighWindWeatherAlertSpec(AlertThresholds thresholds)
    {
        _thresholds = thresholds;

        // Use internal KilometersPerHour property for LINQ expression tree (DB queries + IsSatisfiedBy)
        Query.Where(r => r.WindSpeed.KilometersPerHour > thresholds.HighWindSpeed.KilometersPerHour);
    }

    public override Result<WeatherAlert> CreateAlert(WeatherReading reading)
    {
        var difference = reading.WindSpeed.DifferenceIn(_thresholds.HighWindSpeed, WindSpeedUnit.KilometersPerHour);

        var severity = difference > CriticalDifferenceKmh
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        var message = $"High wind alert: {reading.WindSpeed.Format(WindSpeedUnit.KilometersPerHour)} " +
                      $"(threshold: {_thresholds.HighWindSpeed.Format(WindSpeedUnit.KilometersPerHour)})";

        return WeatherAlert.Create(AlertType.HighWind, severity, message);
    }
}
