using DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.HealthChecks;

/// <summary>
/// Health check that monitors the OutboxRelay service execution.
/// Reports unhealthy if the service hasn't run within the expected time window.
/// </summary>
public sealed class OutboxRelayHealthCheck : IHealthCheck
{
    private readonly OutboxRelayHealthCheckOptions _outboxRelayHealthCheckOptions;
    private readonly TimeSpan _degradedThreshold;
    private readonly TimeSpan _unhealthyThreshold;
    private readonly TimeProvider _timeProvider;
    private readonly OutboxRelayMetrics _outboxRelayMetrics;

    public OutboxRelayHealthCheck(
        IOptions<OutboxRelayHealthCheckOptions> healthCheckOptions,
        IOptions<OutboxRelayOptions> outboxRelayOptions,
        TimeProvider timeProvider,
        OutboxRelayMetrics outboxRelayMetrics)
    {
        _timeProvider = timeProvider;
        _outboxRelayHealthCheckOptions = healthCheckOptions.Value;
        _outboxRelayMetrics = outboxRelayMetrics;

        var pollingIntervalMs = outboxRelayOptions.Value.PollingIntervalMs;

        _degradedThreshold = TimeSpan.FromMilliseconds(Math.Max(
            pollingIntervalMs * _outboxRelayHealthCheckOptions.DegradedThresholdMultiplier,
            _outboxRelayHealthCheckOptions.MinimumDegradedThreshold.TotalMilliseconds));

        _unhealthyThreshold = TimeSpan.FromMilliseconds(Math.Max(
            pollingIntervalMs * _outboxRelayHealthCheckOptions.UnhealthyThresholdMultiplier,
            _outboxRelayHealthCheckOptions.MinimumUnhealthyThreshold.TotalMilliseconds));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // If never executed successfully, check if we're still in a startup grace period
        if (_outboxRelayMetrics.LastSuccessfulExecution == DateTimeOffset.MinValue)
        {
            var uptime = _timeProvider.GetUtcNow() - _outboxRelayHealthCheckOptions.ServiceStartTime;
            if (uptime >= _outboxRelayHealthCheckOptions.StartupGracePeriod)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Service has never completed a successful execution"));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Service is starting up (grace period: {_outboxRelayHealthCheckOptions.StartupGracePeriod.TotalSeconds}s)"));
        }

        var timeSinceLastExecution = _timeProvider.GetUtcNow() - _outboxRelayMetrics.LastSuccessfulExecution;
        if (timeSinceLastExecution > _unhealthyThreshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Service hasn't executed successfully for {timeSinceLastExecution.TotalSeconds:F1} seconds " +
                $"(threshold: {_unhealthyThreshold.TotalSeconds:F1} seconds)"));
        }

        if (timeSinceLastExecution > _degradedThreshold)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Service hasn't executed successfully for {timeSinceLastExecution.TotalSeconds:F1} seconds " +
                $"(threshold: {_degradedThreshold.TotalSeconds:F1} seconds)"));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Service is healthy. Last execution: {timeSinceLastExecution.TotalSeconds:F1} seconds ago"));
    }
}
