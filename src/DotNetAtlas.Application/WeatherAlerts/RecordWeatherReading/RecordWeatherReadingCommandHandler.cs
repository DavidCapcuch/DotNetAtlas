using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;

/// <summary>
/// Handles recording weather readings for monitored locations.
/// Creates value objects from primitive inputs and delegates to the MonitoredLocation aggregate,
/// which evaluates alert conditions and raises WeatherAlertIssuedDomainEvent when thresholds are breached.
/// Processes readings in batches, continuing with valid readings even if some fail validation.
/// </summary>
public sealed class
    RecordWeatherReadingCommandHandler : ICommandHandler<RecordWeatherReadingCommand, BatchRecordingResult>
{
    private readonly IWeatherDbContext _weatherDbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecordWeatherReadingCommandHandler> _logger;

    public RecordWeatherReadingCommandHandler(
        IWeatherDbContext weatherDbContext,
        TimeProvider timeProvider,
        ILogger<RecordWeatherReadingCommandHandler> logger)
    {
        _weatherDbContext = weatherDbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<BatchRecordingResult>> HandleAsync(
        RecordWeatherReadingCommand command,
        CancellationToken ct)
    {
        if (command.Readings.Length == 0)
        {
            _logger.LogDebug(
                "No readings provided for MonitoredLocation {MonitoredLocationId}",
                command.MonitoredLocationId);
            return Result.Ok(new BatchRecordingResult(0, 0, []));
        }

        var monitoredLocation = await _weatherDbContext.MonitoredLocations
            .Include(ml => ml.Location)
            .FirstOrDefaultAsync(ml => ml.Id == command.MonitoredLocationId, ct);

        if (monitoredLocation is null)
        {
            _logger.LogWarning(
                "MonitoredLocation not found for ID {MonitoredLocationId}, cannot record readings",
                command.MonitoredLocationId);
            return Result.Fail<BatchRecordingResult>(
                MonitoredLocationErrors.MonitoredLocationNotFound(command.MonitoredLocationId));
        }

        var successCount = 0;
        var failedCount = 0;
        var failures = new List<ReadingFailure>();

        for (var i = 0; i < command.Readings.Length; i++)
        {
            var readingDto = command.Readings[i];

            var temperatureResult = Temperature.FromCelsius(readingDto.TemperatureC);
            var humidityResult = Humidity.FromPercent(readingDto.HumidityPercent);
            var windSpeedResult = WindSpeed.FromKilometersPerHour(readingDto.WindSpeedKmh);

            var mergedResult = Result.Merge(temperatureResult, humidityResult, windSpeedResult);
            if (mergedResult.IsFailed)
            {
                failedCount++;
                failures.Add(new ReadingFailure(i, readingDto, mergedResult.Errors.Select(e => e.Message)));

                _logger.LogWarning(
                    "Invalid weather reading at index {Index} for MonitoredLocation {MonitoredLocationId}: {Errors}",
                    i, command.MonitoredLocationId, mergedResult.Errors.ToErrorsSummary());

                continue;
            }

            var weatherReading = WeatherReading.Create(
                temperatureResult.Value, humidityResult.Value, windSpeedResult.Value, readingDto.RecordedAtUtc);

            var utcNow = _timeProvider.GetUtcNow();
            monitoredLocation.RecordWeatherReading(weatherReading, utcNow);
            successCount++;
        }

        if (successCount > 0)
        {
            // Save all valid readings at once
            await _weatherDbContext.SaveChangesAsync(ct);
        }

        var batchResult = new BatchRecordingResult(successCount, failedCount, failures);

        _logger.LogInformation(
            "Batch recording completed for MonitoredLocation {MonitoredLocationId} ({City}, {CountryCode}): " +
            "{SuccessCount} successful, {FailedCount} failed",
            command.MonitoredLocationId, monitoredLocation.Location.City.Name,
            monitoredLocation.Location.CountryCode, successCount, failedCount);

        return Result.Ok(batchResult);
    }
}
