using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Alerts.Events;
using DotNetAtlas.Domain.Alerts.Specifications.AlertConditions;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.SharedKernel.Base;

namespace DotNetAtlas.Domain.Alerts;

/// <summary>
/// Aggregate root representing a geographic location being monitored for weather conditions.
/// Owns weather readings, alert thresholds, and is responsible for issuing alerts when thresholds are breached.
/// </summary>
/// <remarks>
/// This aggregate can raise the following domain events:
/// <list type="bullet">
/// <item><see cref="MonitoredLocationCreatedDomainEvent"/>: When a new monitored location is created.</item>
/// <item><see cref="WeatherAlertIssuedDomainEvent"/>: When a weather reading breaches alert thresholds.</item>
/// </list>
/// </remarks>
public sealed class MonitoredLocation : AggregateRoot<Guid>, IAuditableEntity
{
    private const int MaxStoredReadings = 24; // Keep last 24 readings for trend analysis

    /// <summary>
    /// The geographic location being monitored (composition - owned by this aggregate).
    /// </summary>
    public Location Location { get; private set; } = null!;

    /// <summary>
    /// Configurable alert thresholds for this location.
    /// </summary>
    public AlertThresholds Thresholds { get; private set; } = null!;

    /// <summary>
    /// Whether this location is actively being monitored.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Recent weather readings for trend analysis.
    /// Limited to the last N readings to prevent unbounded growth.
    /// </summary>
    private readonly List<WeatherReading> _readings = [];

    public IReadOnlyCollection<WeatherReading> RecentReadings => _readings;

    private MonitoredLocation()
    {
    }

    /// <summary>
    /// Creates a new monitored location with default thresholds.
    /// </summary>
    /// <param name="location">The geographic location to monitor.</param>
    /// <returns>A new monitored location with default alert thresholds.</returns>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="MonitoredLocationCreatedDomainEvent"/>: Always raised when created.</item>
    /// </list>
    /// </remarks>
    public static MonitoredLocation CreateWithDefaultThresholds(Location location)
    {
        return Create(location, AlertThresholds.CreateDefault());
    }

    /// <summary>
    /// Creates a new monitored location with custom thresholds.
    /// </summary>
    /// <param name="location">The geographic location to monitor.</param>
    /// <param name="thresholds">Custom alert thresholds for this location.</param>
    /// <returns>A new monitored location.</returns>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="MonitoredLocationCreatedDomainEvent"/>: Always raised when created.</item>
    /// </list>
    /// </remarks>
    public static MonitoredLocation Create(Location location, AlertThresholds thresholds)
    {
        var monitoredLocation = new MonitoredLocation
        {
            Id = Guid.CreateVersion7(),
            Location = location,
            Thresholds = thresholds,
            IsActive = true
        };

        monitoredLocation.AddDomainEvent(new MonitoredLocationCreatedDomainEvent
        {
            MonitoredLocationId = monitoredLocation.Id,
            City = location.City,
            CountryCode = location.CountryCode
        });

        return monitoredLocation;
    }

    /// <summary>
    /// Records a new weather reading and evaluates alert conditions.
    /// If any thresholds are breached, domain events are raised for each alert.
    /// </summary>
    /// <param name="weatherReading">The weather reading to record.</param>
    /// <param name="utcNow">Current UTC time for alert timestamps.</param>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="WeatherAlertIssuedDomainEvent"/>: Raised for each alert condition that is triggered.</item>
    /// </list>
    /// </remarks>
    public void RecordWeatherReading(WeatherReading weatherReading, DateTimeOffset utcNow)
    {
        _readings.Add(weatherReading);
        TrimOldReadings();

        if (!IsActive)
        {
            return;
        }

        var weatherAlerts = EvaluateAlertConditions(weatherReading);

        foreach (var weatherAlert in weatherAlerts)
        {
            AddDomainEvent(new WeatherAlertIssuedDomainEvent
            {
                MonitoredLocationId = Id,
                City = Location.City,
                CountryCode = Location.CountryCode,
                WeatherAlert = weatherAlert,
                TriggeringReading = weatherReading,
                IssuedAtUtc = utcNow
            });
        }
    }

    public void UpdateThresholds(AlertThresholds thresholds) => Thresholds = thresholds;

    public void ActivateMonitoring() => IsActive = true;

    /// <summary>
    /// Deactivates monitoring for this location.
    /// Weather readings will still be recorded but no alerts will be issued.
    /// </summary>
    public void DeactivateMonitoring() => IsActive = false;

    /// <summary>
    /// Evaluates a weather reading against all alert condition specifications.
    /// Uses Ardalis.Specification pattern for clean, testable, and extensible alert rules.
    /// </summary>
    private IEnumerable<WeatherAlert> EvaluateAlertConditions(WeatherReading reading)
    {
        // Each specification encapsulates its own evaluation logic and severity determination.
        var alertSpecsToEvaluate = (IReadOnlyList<WeatherAlertSpec>)
        [
            new HighTemperatureWeatherAlertSpec(Thresholds),
            new LowTemperatureWeatherAlertSpec(Thresholds),
            new HighWindWeatherAlertSpec(Thresholds),
            new HighHumidityWeatherAlertSpec(Thresholds),
            new LowHumidityWeatherAlertSpec(Thresholds)
        ];

        foreach (var spec in alertSpecsToEvaluate)
        {
            if (spec.IsSatisfiedBy(reading))
            {
                var alertResult = spec.CreateAlert(reading);

                // Message validation should never fail for internally generated messages,
                // but we handle it gracefully by skipping failed alerts.
                if (alertResult.IsSuccess)
                {
                    yield return alertResult.Value;
                }
            }
        }
    }

    private void TrimOldReadings()
    {
        if (_readings.Count <= MaxStoredReadings)
        {
            return;
        }

        // Remove oldest readings from the beginning.
        // Using RemoveRange is O(n) but only called once, vs O(n) per RemoveAt(0) in a loop.
        var countToRemove = _readings.Count - MaxStoredReadings;
        _readings.RemoveRange(0, countToRemove);
    }

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset LastModifiedUtc { get; private set; }
}
