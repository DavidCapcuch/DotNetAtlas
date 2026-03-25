namespace Weather.Domain.Alerts.ValueObjects;

/// <summary>
/// Types of weather alerts that can be issued.
/// </summary>
public enum AlertType
{
    HighTemperature,
    LowTemperature,
    HighWind,
    HighHumidity,
    LowHumidity
}
