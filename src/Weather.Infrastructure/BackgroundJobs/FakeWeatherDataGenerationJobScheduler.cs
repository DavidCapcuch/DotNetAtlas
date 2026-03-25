using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weather.Application.WeatherAlerts.Common.Abstractions;
using Weather.Infrastructure.BackgroundJobs.Config;
using Weather.Infrastructure.BackgroundJobs.WeatherAlerts;

namespace Weather.Infrastructure.BackgroundJobs;

internal sealed class FakeWeatherDataGenerationJobScheduler : IFakeWeatherDataGenerationJobScheduler
{
    private readonly ILogger<FakeWeatherDataGenerationJobScheduler> _logger;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly FakeWeatherDataGeneratorBackgroundJobOptions _fakeWeatherDataGeneratorBackgroundJobOptions;

    public FakeWeatherDataGenerationJobScheduler(
        IRecurringJobManager recurringJobManager,
        IOptions<FakeWeatherDataGeneratorBackgroundJobOptions> fakeWeatherAlertJobOptions,
        ILogger<FakeWeatherDataGenerationJobScheduler> logger)
    {
        _recurringJobManager = recurringJobManager;
        _fakeWeatherDataGeneratorBackgroundJobOptions = fakeWeatherAlertJobOptions.Value;
        _logger = logger;
    }

    public void EnsureWeatherGenerationJobSchedule(Guid monitoredLocationId)
    {
        var recurringJobId = FakeWeatherDataGeneratorBackgroundJob.JobId(monitoredLocationId);
        _recurringJobManager.AddOrUpdate<FakeWeatherDataGeneratorBackgroundJob>(
            recurringJobId: recurringJobId,
            methodCall: job =>
                job.GenerateWeatherReadingsBatch(monitoredLocationId,
                    _fakeWeatherDataGeneratorBackgroundJobOptions.BatchSize,
                    CancellationToken.None), // hangfire resolves cancellation token on its own
            cronExpression: _fakeWeatherDataGeneratorBackgroundJobOptions.Cron,
            queue: _fakeWeatherDataGeneratorBackgroundJobOptions.Queue);

        _logger.LogInformation(
            "Scheduled fake data generation job MonitoredLocationId {MonitoredLocationId} on queue {Queue}",
            monitoredLocationId, _fakeWeatherDataGeneratorBackgroundJobOptions.Queue);
    }

    public void TriggerFakeWeatherDataGenerationJob(Guid monitoredLocationId)
    {
        var recurringJobId = FakeWeatherDataGeneratorBackgroundJob.JobId(monitoredLocationId);
        _recurringJobManager.Trigger(recurringJobId);
        _logger.LogInformation(
            "Triggered fake data generation job for MonitoredLocationId {MonitoredLocationId}", monitoredLocationId);
    }
}
