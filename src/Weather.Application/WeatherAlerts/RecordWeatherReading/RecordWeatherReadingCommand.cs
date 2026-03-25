using Platform.CQS;

namespace Weather.Application.WeatherAlerts.RecordWeatherReading;

/// <summary>
/// Command to record weather readings for a monitored location.
/// When the readings are recorded, the MonitoredLocation aggregate evaluates
/// alert conditions and may issue alerts via domain events.
/// </summary>
public class RecordWeatherReadingCommand : ICommand<BatchRecordingResult>
{
    /// <summary>
    /// The ID of the monitored location to record the readings for.
    /// </summary>
    public required Guid MonitoredLocationId { get; set; }

    /// <summary>
    /// Array of weather readings to record.
    /// </summary>
    public required WeatherReadingDto[] Readings { get; set; }
}
