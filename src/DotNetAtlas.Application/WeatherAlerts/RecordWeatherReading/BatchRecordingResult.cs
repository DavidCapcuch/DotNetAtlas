namespace DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;

/// <summary>
/// Result of batch recording weather readings.
/// </summary>
public sealed class BatchRecordingResult
{
    /// <summary>
    /// Number of readings that were successfully recorded.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of readings that failed validation.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Details about failed readings with their indices in the original array.
    /// </summary>
    public IReadOnlyList<ReadingFailure> Failures { get; init; } = [];

    public BatchRecordingResult(int successCount, int failedCount, IEnumerable<ReadingFailure> failures)
    {
        SuccessCount = successCount;
        FailedCount = failedCount;
        Failures = failures.ToList().AsReadOnly();
    }

    public static BatchRecordingResult Empty => new(0, 0, []);
}

/// <summary>
/// Details about a failed weather reading.
/// </summary>
public sealed class ReadingFailure
{
    /// <summary>
    /// Index of the failed reading in the original array.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// The reading that failed validation.
    /// </summary>
    public WeatherReadingDto Reading { get; init; } = null!;

    /// <summary>
    /// Validation errors for the reading.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    public ReadingFailure(int index, WeatherReadingDto reading, IEnumerable<string> errors)
    {
        Index = index;
        Reading = reading;
        Errors = errors.ToList().AsReadOnly();
    }
}
