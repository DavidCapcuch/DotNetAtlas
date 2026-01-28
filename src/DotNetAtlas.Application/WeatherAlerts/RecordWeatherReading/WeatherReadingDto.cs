namespace DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;

/// <summary>
/// DTO representing a weather reading.
/// </summary>
public sealed class WeatherReadingDto
{
    /// <summary>
    /// Temperature in Celsius.
    /// </summary>
    public required double TemperatureC { get; set; }

    /// <summary>
    /// Relative humidity percentage (0-100).
    /// </summary>
    public required double HumidityPercent { get; set; }

    /// <summary>
    /// Wind speed in kilometers per hour.
    /// </summary>
    public required double WindSpeedKmh { get; set; }

    /// <summary>
    /// UTC timestamp when the reading was recorded by the sensor.
    /// </summary>
    public required DateTimeOffset RecordedAtUtc { get; set; }
}
