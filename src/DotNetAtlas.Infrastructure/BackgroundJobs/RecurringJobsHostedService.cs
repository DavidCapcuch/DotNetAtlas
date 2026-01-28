using DotNetAtlas.Infrastructure.BackgroundJobs.Config;
using DotNetAtlas.Infrastructure.BackgroundJobs.WeatherAlerts;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.BackgroundJobs;

/// <summary>
/// Hosted service that registers recurring background jobs at application startup.
/// </summary>
/// <remarks>
/// <para>
/// <b>DEMO ONLY - Startup Cleanup:</b> This service removes all orphaned
/// <see cref="FakeWeatherDataGeneratorBackgroundJob"/> jobs on startup. This is necessary because:
/// </para>
/// <list type="bullet">
/// <item>When the application restarts (crash, deployment, pod restart), SignalR connections are lost</item>
/// <item><c>OnDisconnectedAsync</c> is NOT called during abrupt shutdowns</item>
/// <item>Hangfire jobs persist in the database and would continue running forever</item>
/// <item>Redis group membership data may be stale or cleared</item>
/// </list>
/// <para>
/// <b>Result:</b> Without cleanup, orphaned jobs would generate fake weather data for groups
/// with zero real subscribers.
/// </para>
/// <para>
/// <b>Production Alternative:</b> For production systems, consider:
/// </para>
/// <list type="bullet">
/// <item>Reconciliation job that periodically checks if jobs have actual subscribers</item>
/// <item>Job self-check that unschedules itself when no subscribers exist</item>
/// <item>Redis key expiry with heartbeat mechanism</item>
/// </list>
/// </remarks>
internal sealed class RecurringJobsHostedService : IHostedService
{
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IBackgroundJobClientV2 _backgroundJobClient;
    private readonly ExpiredSubscriptionsJobOptions _expiredSubscriptionsOptions;
    private readonly ILogger<RecurringJobsHostedService> _logger;

    public RecurringJobsHostedService(
        IRecurringJobManager recurringJobManager,
        IBackgroundJobClientV2 backgroundJobClient,
        IOptions<ExpiredSubscriptionsJobOptions> expiredSubscriptionsOptions,
        ILogger<RecurringJobsHostedService> logger)
    {
        _recurringJobManager = recurringJobManager;
        _backgroundJobClient = backgroundJobClient;
        _expiredSubscriptionsOptions = expiredSubscriptionsOptions.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Registering recurring background jobs");

        // Clean up orphaned fake weather jobs from previous app instance.
        // SignalR connections are lost on restart but Hangfire jobs persist.
        CleanupFakeWeatherDataGeneratorJobs();

        _recurringJobManager.AddOrUpdate<ExpiredSubscriptionsBackgroundJob>(
            recurringJobId: ExpiredSubscriptionsBackgroundJob.JobId,
            methodCall: job => job.ProcessExpiredSubscriptionsAsync(CancellationToken.None),
            cronExpression: _expiredSubscriptionsOptions.Cron,
            queue: _expiredSubscriptionsOptions.Queue);

        _logger.LogInformation(
            "Registered {JobId} with cron '{Cron}' on queue '{Queue}'",
            ExpiredSubscriptionsBackgroundJob.JobId,
            _expiredSubscriptionsOptions.Cron,
            _expiredSubscriptionsOptions.Queue);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes all fake weather data generator jobs on startup.
    /// </summary>
    /// <remarks>
    /// <b>DEMO ONLY:</b> Simple cleanup that removes all jobs regardless of active subscribers.
    /// Clients will recreate jobs when they reconnect.
    /// </remarks>
    private void CleanupFakeWeatherDataGeneratorJobs()
    {
        using var connection = _backgroundJobClient.Storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        var fakeWeatherJobs = recurringJobs
            .Where(job => job.Id.StartsWith(nameof(FakeWeatherDataGeneratorBackgroundJob), StringComparison.Ordinal))
            .ToList();

        if (fakeWeatherJobs.Count == 0)
        {
            _logger.LogDebug("No fake weather jobs to clean up");
            return;
        }

        foreach (var job in fakeWeatherJobs)
        {
            _recurringJobManager.RemoveIfExists(job.Id);
        }

        _logger.LogInformation(
            "Cleaned {Count} fake weather jobs on startup", fakeWeatherJobs.Count);
    }
}
