using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.Jobs;

internal sealed class HangfireWeatherAlertJobScheduler : IWeatherAlertJobScheduler
{
    private readonly ILogger<HangfireWeatherAlertJobScheduler> _logger;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly FakeWeatherAlertJobOptions _fakeWeatherAlertJobOptions;

    public HangfireWeatherAlertJobScheduler(
        IRecurringJobManager recurringJobManager,
        IOptions<FakeWeatherAlertJobOptions> fakeWeatherAlertJobOptions,
        ILogger<HangfireWeatherAlertJobScheduler> logger)
    {
        _recurringJobManager = recurringJobManager;
        _fakeWeatherAlertJobOptions = fakeWeatherAlertJobOptions.Value;
        _logger = logger;
    }

    public void ScheduleAlertJobForGroup(AlertSubscriptionDto alertSubscriptionDto, string groupName)
    {
        var recurringJobId = FakeWeatherAlertJob.JobId(groupName);
        _recurringJobManager.AddOrUpdate<FakeWeatherAlertJob>(
            recurringJobId: recurringJobId,
            methodCall: job =>
                job.SendWeatherAlert(alertSubscriptionDto,
                    CancellationToken.None), // hangfire resolves cancellation token on its own
            cronExpression: _fakeWeatherAlertJobOptions.Cron);
        _logger.LogInformation("Scheduled alerts for {Group}", groupName);
    }

    public void TriggerAlertJobForGroup(AlertSubscriptionDto alertSubscriptionDto, string groupName)
    {
        var recurringJobId = FakeWeatherAlertJob.JobId(groupName);
        _recurringJobManager.Trigger(recurringJobId);
        _logger.LogInformation("Triggered alert job for {Group}", groupName);
    }

    public void RemoveAlertJobForGroup(string groupName)
    {
        var recurringJobId = FakeWeatherAlertJob.JobId(groupName);
        _recurringJobManager.RemoveIfExists(recurringJobId);
        _logger.LogInformation("Removed alert job for {Group}", groupName);
    }
}
