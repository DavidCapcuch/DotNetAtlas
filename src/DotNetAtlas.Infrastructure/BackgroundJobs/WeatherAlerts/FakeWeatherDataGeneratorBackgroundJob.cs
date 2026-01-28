using DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;
using DotNetAtlas.CQS;
using DotNetAtlas.Infrastructure.BackgroundJobs.Common;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Infrastructure.BackgroundJobs.WeatherAlerts;

/// <summary>
/// Simulates a physical weather station by generating realistic weather sensor data.
/// The data is sent to MonitoredLocation aggregates via RecordWeatherReadingCommand,
/// where the domain logic evaluates alert conditions and issues alerts as needed.
/// </summary>
[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail, LogEvents = true)]
[DisableConcurrentExecution(15)]
internal sealed class FakeWeatherDataGeneratorBackgroundJob : IBackgroundJob
{
    /// <summary>
    /// Generates a unique job ID based on the group name.
    /// Using group name allows unscheduling jobs without needing the MonitoredLocationId.
    /// </summary>
    /// <param name="monitoredLocationId">The Monitored Location Id.</param>
    /// <returns>A unique job ID for Hangfire.</returns>
    public static string JobId(Guid monitoredLocationId) =>
        $"{nameof(FakeWeatherDataGeneratorBackgroundJob)}-{monitoredLocationId}";

    private readonly ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult> _recordWeatherReadingHandler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FakeWeatherDataGeneratorBackgroundJob> _logger;

    public FakeWeatherDataGeneratorBackgroundJob(
        ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult> recordWeatherReadingHandler,
        TimeProvider timeProvider,
        ILogger<FakeWeatherDataGeneratorBackgroundJob> logger)
    {
        _recordWeatherReadingHandler = recordWeatherReadingHandler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Generates and records a batch of weather readings for a specific monitored location.
    /// </summary>
    /// <param name="monitoredLocationId">The ID of the monitored location to generate readings for.</param>
    /// <param name="batchSize">The number of readings to generate in this batch.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task GenerateWeatherReadingsBatch(Guid monitoredLocationId, int batchSize, CancellationToken ct)
    {
        var readings = GenerateReadingsBatch(batchSize, _timeProvider.GetUtcNow());

        _logger.LogDebug(
            "FakeWeatherStation generating batch of {BatchSize} readings for MonitoredLocation {MonitoredLocationId}",
            batchSize, monitoredLocationId);

        var result = await _recordWeatherReadingHandler.HandleAsync(
            new RecordWeatherReadingCommand
            {
                MonitoredLocationId = monitoredLocationId,
                Readings = readings
            }, ct);

        if (result.IsSuccess)
        {
            var batchResult = result.Value;
            _logger.LogInformation(
                "Batch recording completed for MonitoredLocation {MonitoredLocationId}: " +
                "{SuccessCount} successful, {FailedCount} failed",
                monitoredLocationId, batchResult.SuccessCount, batchResult.FailedCount);
        }
    }

    /// <summary>
    /// Generates a batch of weather readings, guaranteeing at least one reading will trigger an alert.
    /// The first reading always exceeds default thresholds for demo purposes.
    /// </summary>
    /// <param name="batchSize">The number of readings to generate.</param>
    /// <param name="readingTime">The timestamp to use for the readings.</param>
    /// <returns>Array of weather reading DTOs.</returns>
    internal static WeatherReadingDto[] GenerateReadingsBatch(int batchSize, DateTimeOffset readingTime)
    {
        var readings = new WeatherReadingDto[batchSize];

        for (var i = 0; i < batchSize; i++)
        {
            // First reading always triggers an alert for demo purposes
            var reading = i == 0
                ? GenerateAlertTriggeringReading()
                : GenerateRealisticReading();

            readings[i] = new WeatherReadingDto
            {
                TemperatureC = reading.TemperatureC,
                HumidityPercent = reading.HumidityPercent,
                WindSpeedKmh = reading.WindSpeedKmh,
                RecordedAtUtc = readingTime
            };
        }

        return readings;
    }

    /// <summary>
    /// Generates a reading that is guaranteed to trigger at least one alert.
    /// Uses high temperature (40°C) which exceeds the default threshold (35°C).
    /// For demo purposes, this ensures users always see alert activity.
    /// </summary>
    internal static WeatherReadingData GenerateAlertTriggeringReading()
    {
        // High temperature guaranteed to exceed default 35°C threshold
        // Using 40°C to clearly trigger a Warning alert (Critical at 40°C+ difference)
        const double alertTriggeringTemperature = 40.0;

        return new WeatherReadingData
        {
            TemperatureC = alertTriggeringTemperature,
            HumidityPercent = 50.0, // Normal humidity
            WindSpeedKmh = 20.0 // Normal wind
        };
    }

    /// <summary>
    /// Generates realistic weather sensor data.
    /// Values are randomized within realistic ranges, with occasional extreme values
    /// that may trigger alerts.
    /// </summary>
    private static WeatherReadingData GenerateRealisticReading()
    {
        // Base values with realistic ranges
        var baseTemp = Random.Shared.Next(-5, 30);
        var baseHumidity = Random.Shared.Next(30, 80);
        var baseWind = Random.Shared.Next(0, 40);

        // Occasionally generate extreme values (10% chance for each)
        var temperature = Random.Shared.NextDouble() < 0.1
            ? Random.Shared.Next(-15, 45) // Extreme temperature
            : baseTemp + (Random.Shared.NextDouble() * 5);

        var humidity = Random.Shared.NextDouble() < 0.1
            ? Random.Shared.Next(10, 100) // Extreme humidity
            : baseHumidity + (Random.Shared.NextDouble() * 10);

        var windSpeed = Random.Shared.NextDouble() < 0.1
            ? Random.Shared.Next(60, 120) // Extreme wind (storm)
            : baseWind + (Random.Shared.NextDouble() * 15);

        return new WeatherReadingData
        {
            TemperatureC = Math.Round(temperature, 1),
            HumidityPercent = Math.Clamp(Math.Round(humidity, 1), 0, 100),
            WindSpeedKmh = Math.Round(windSpeed, 1)
        };
    }

    internal sealed record WeatherReadingData
    {
        public required double TemperatureC { get; init; }
        public required double HumidityPercent { get; init; }
        public required double WindSpeedKmh { get; init; }
    }
}
